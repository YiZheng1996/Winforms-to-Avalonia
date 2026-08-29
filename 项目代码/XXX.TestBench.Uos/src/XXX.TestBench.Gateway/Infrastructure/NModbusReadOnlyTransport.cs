using System.IO.Ports;
using System.Net.Sockets;
using NModbus;
using NModbus.Serial;
using XXX.TestBench.Gateway.Domain;

namespace XXX.TestBench.Gateway.Infrastructure;

public sealed class NModbusReadOnlyTransport : IReadOnlyModbusTransport
{
    private readonly ModbusRtuOptions? _rtuOptions;
    private readonly ModbusTcpOptions? _tcpOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcpClient;
    private SerialPort? _serialPort;
    private IModbusMaster? _master;
    private long _connectionGeneration;

    public NModbusReadOnlyTransport(ModbusRtuOptions options)
    {
        _rtuOptions = options ?? throw new ArgumentNullException(nameof(options));
        _rtuOptions.Validate();
        TransportId = $"Modbus-RTU:{_rtuOptions.PortName}";
    }

    public NModbusReadOnlyTransport(ModbusTcpOptions options)
    {
        _tcpOptions = options ?? throw new ArgumentNullException(nameof(options));
        _tcpOptions.Validate();
        TransportId = $"Modbus-TCP:{_tcpOptions.Host}:{_tcpOptions.Port}";
    }

    public string TransportId { get; }
    public bool IsConnected => _master is not null;
    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
                return;

            var factory = new ModbusFactory();
            if (_tcpOptions is not null)
            {
                var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(_tcpOptions.Host, _tcpOptions.Port, cancellationToken).ConfigureAwait(false);
                    _tcpClient = client;
                    _master = factory.CreateMaster(client);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }
            else
            {
                var options = _rtuOptions ?? throw new InvalidOperationException("Modbus 配置为空。");
                var port = new SerialPort(options.PortName, options.BaudRate, options.Parity, options.DataBits, options.StopBits)
                {
                    ReadTimeout = options.ReadTimeoutMs,
                    WriteTimeout = options.ReadTimeoutMs
                };
                try
                {
                    await Task.Run(port.Open, cancellationToken).ConfigureAwait(false);
                    _serialPort = port;
                    _master = factory.CreateRtuMaster(port);
                }
                catch
                {
                    port.Dispose();
                    throw;
                }
            }

            Interlocked.Increment(ref _connectionGeneration);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ModbusRegisterReadResult> ReadRegistersAsync(
        ModbusRegisterReadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RegisterCount is 0 or > 125)
            throw new ArgumentOutOfRangeException(nameof(request), "Modbus 单次读取寄存器数必须为 1 到 125。");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var master = _master ?? throw new InvalidOperationException("Modbus 尚未连接。");
            var unitId = request.UnitId;
            var registers = request.Area switch
            {
                ModbusRegisterArea.HoldingRegisters => await master.ReadHoldingRegistersAsync(unitId, request.StartAddress, request.RegisterCount).WaitAsync(cancellationToken).ConfigureAwait(false),
                ModbusRegisterArea.InputRegisters => await master.ReadInputRegistersAsync(unitId, request.StartAddress, request.RegisterCount).WaitAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            };

            return new ModbusRegisterReadResult(
                request,
                registers,
                DataQuality.Good,
                DateTimeOffset.UtcNow,
                ConnectionGeneration);
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or SlaveException)
        {
            return new ModbusRegisterReadResult(
                request,
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
            _master?.Dispose();
            _master = null;
            _tcpClient?.Dispose();
            _tcpClient = null;
            _serialPort?.Dispose();
            _serialPort = null;
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
}
