using S7.Net;
using S7.Net.Types;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using XXX.TestBench.Gateway.Domain;

namespace XXX.TestBench.Gateway.Infrastructure;

public sealed class S7NetPlusReadOnlyTransport : IReadOnlyS7Transport
{
    private readonly S7EndpointOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Plc? _plc;
    private long _connectionGeneration;

    public S7NetPlusReadOnlyTransport(S7EndpointOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        EndpointId = _options.Id;
    }

    public string EndpointId { get; }
    public bool IsConnected => _plc?.IsConnected == true;
    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
                return;

            var plc = new Plc(
                ParseCpuType(_options.Model),
                _options.IpAddress,
                _options.Port,
                _options.Rack,
                _options.Slot)
            {
                ReadTimeout = _options.ReadTimeoutMs,
                WriteTimeout = _options.ReadTimeoutMs
            };

            try
            {
                await Task.Run(plc.Open, cancellationToken).ConfigureAwait(false);
                if (!plc.IsConnected)
                    throw new IOException($"S7 设备 {EndpointId} 未建立连接。");

                _plc = plc;
                Interlocked.Increment(ref _connectionGeneration);
            }
            catch
            {
                ((IDisposable)plc).Dispose();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<S7RawReadResult> ReadMemoryAsync(
        string address,
        int byteCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("S7 地址不能为空。", nameof(address));
        if (byteCount is < 1 or > 2_000)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plc = _plc ?? throw new InvalidOperationException("S7 尚未连接。");
            var parsed = S7MemoryAddress.Parse(address);
            var raw = await Task.Run(
                () => plc.ReadBytes(parsed.Area, parsed.DataBlockNumber, parsed.ByteAddress, byteCount),
                cancellationToken).ConfigureAwait(false);

            if (parsed.BitIndex is not null)
            {
                var bit = (raw[0] & (1 << parsed.BitIndex.Value)) != 0;
                raw = [bit ? (byte)1 : (byte)0];
            }

            return new S7RawReadResult(
                address,
                raw,
                DataQuality.Good,
                DateTimeOffset.UtcNow,
                ConnectionGeneration);
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or InvalidOperationException)
        {
            return new S7RawReadResult(
                address,
                [],
                DataQuality.Bad,
                DateTimeOffset.UtcNow,
                ConnectionGeneration,
                ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _plc?.Close();
            if (_plc is IDisposable disposable)
                disposable.Dispose();
            _plc = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private static CpuType ParseCpuType(string model)
    {
        var enumName = model.Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        return Enum.TryParse<CpuType>(enumName, ignoreCase: true, out var cpu)
            ? cpu
            : throw new OptionsValidationException($"S7.NetPlus 不支持的 CPU 型号: {model}");
    }

    private sealed record S7MemoryAddress(
        DataType Area,
        int DataBlockNumber,
        int ByteAddress,
        int? BitIndex)
    {
        private static readonly Regex DataBlockPattern = new(
            @"^DB(?<db>\d+)\.DB(?<kind>[XBWD])(?<address>\d+)(?:\.(?<bit>\d+))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static S7MemoryAddress Parse(string address)
        {
            var value = address.Trim().ToUpperInvariant();

            var dbMatch = DataBlockPattern.Match(value);
            if (dbMatch.Success)
            {
                var kind = dbMatch.Groups["kind"].Value;
                var bit = dbMatch.Groups["bit"].Success
                    ? (int?)ParseBit(dbMatch.Groups["bit"].Value, address)
                    : null;
                if (kind != "X" && bit is not null)
                    throw new FormatException($"只有 DBX 地址允许位索引: {address}");

                return new S7MemoryAddress(
                    DataType.DataBlock,
                    ParseNumber(dbMatch.Groups["db"].Value, address),
                    ParseNumber(dbMatch.Groups["address"].Value, address),
                    bit);
            }

            var area = value[0] switch
            {
                'V' or 'M' => DataType.Memory,
                'I' => DataType.Input,
                'Q' => DataType.Output,
                _ => throw new FormatException($"不支持的 S7 地址区: {address}")
            };

            var body = value[1..];
            var bitSeparator = body.IndexOf('.');
            if (bitSeparator >= 0)
            {
                var byteAddress = ParseNumber(body[..bitSeparator], address);
                var bitIndex = ParseBit(body[(bitSeparator + 1)..], address);
                if (byteAddress < 0)
                    throw new FormatException($"无效的 S7 位地址: {address}");
                return new S7MemoryAddress(area, 0, byteAddress, bitIndex);
            }

            var prefix = body.Length >= 1 && body[0] is 'B' or 'W' or 'D' ? body[0] : '\0';
            var number = prefix == '\0' ? body : body[1..];
            var rawAddress = ParseNumber(number, address);

            return new S7MemoryAddress(area, 0, rawAddress, null);
        }

        private static int ParseNumber(string value, string source)
        {
            if (!int.TryParse(value, out var number) || number < 0)
                throw new FormatException($"无效的 S7 数字地址: {source}");
            return number;
        }

        private static int ParseBit(string value, string source)
        {
            var bit = ParseNumber(value, source);
            if (bit > 7)
                throw new FormatException($"S7 位索引必须在 0 到 7 之间: {source}");
            return bit;
        }
    }
}
