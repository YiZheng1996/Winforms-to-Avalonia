using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// 设备配置版本目录存储：先写完整 revision，再原子切换 active 指针。
/// </summary>
public sealed class DeviceConfigurationStore : IDeviceConfigurationStore, IDeviceConfigurationMigrationSource, IAsyncDisposable
{
    private const int ManifestSchemaVersion = 1;
    private static readonly IReadOnlySet<string> RequiredRevisionFiles = new HashSet<string>(StringComparer.Ordinal)
    {
        "device.json",
        "points.json",
        "simulation.json",
        "signal-bindings.json"
    };
    private const string ActiveFileName = "active.json";
    private const string PreviousActiveFileName = "active.previous.json";
    private readonly string _root;
    private readonly string _revisionsRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public DeviceConfigurationStore(string configRoot)
    {
        if (string.IsNullOrWhiteSpace(configRoot)) throw new ArgumentException("配置根目录不能为空", nameof(configRoot));
        _root = Path.Combine(Path.GetFullPath(configRoot), "device-config");
        _revisionsRoot = Path.Combine(_root, "revisions");
    }

    /// <summary>
    /// 如果最近一次加载使用了上一生效指针，则暴露恢复说明供上层审计。
    /// </summary>
    public string? LastRecoveryMessage { get; private set; }

    public async Task<DeviceConfigurationSnapshot> LoadActiveAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            LastRecoveryMessage = null;
            var failures = new List<string>();
            foreach (var pointerName in new[] { ActiveFileName, PreviousActiveFileName })
            {
                try
                {
                    var pointer = await ReadPointerAsync(pointerName, ct);
                    var snapshot = await LoadRevisionCoreAsync(pointer.Revision, ct);
                    if (pointerName == PreviousActiveFileName)
                        LastRecoveryMessage = $"当前生效指针损坏，已恢复上一版本：{pointer.Revision}";
                    return snapshot;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ConfigValidationException)
                {
                    failures.Add($"{pointerName}：{ex.Message}");
                }
            }

            throw new ConfigValidationException("没有可验证的生效设备配置：" + string.Join("；", failures));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 迁移专用读取：验证 active/previous 指针、manifest、文件集合和哈希，保留旧 schema 供上层显式迁移。
    /// </summary>
    public async Task<DeviceConfigurationSnapshot> LoadActiveForMigrationAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var failures = new List<string>();
            foreach (var pointerName in new[] { ActiveFileName, PreviousActiveFileName })
            {
                try
                {
                    var pointer = await ReadPointerAsync(pointerName, ct);
                    return await LoadRevisionCoreAsync(pointer.Revision, ct, validateSemantics: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ConfigValidationException)
                {
                    failures.Add($"{pointerName}：{ex.Message}");
                }
            }
            throw new ConfigValidationException("没有可迁移读取的生效设备配置：" + string.Join("；", failures));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DeviceConfigurationSnapshot> LoadRevisionAsync(string revision, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await LoadRevisionCoreAsync(revision, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StageAsync(DeviceConfigurationSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateRevision(snapshot.Revision);
        MultiDeviceConfigurationValidator.EnsureValid(snapshot);

        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Directory.CreateDirectory(_revisionsRoot);
            var finalDirectory = RevisionDirectory(snapshot.Revision);
            if (Directory.Exists(finalDirectory))
            {
                // revision 不可变：已有内容必须可验证，不允许原地覆盖。
                _ = await LoadRevisionCoreAsync(snapshot.Revision, ct);
                return;
            }

            var stagingDirectory = Path.Combine(_revisionsRoot,
                "." + snapshot.Revision + ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            try
            {
                var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["device.json"] = Serialize(snapshot.Device),
                    ["points.json"] = Serialize(snapshot.Points),
                    ["simulation.json"] = Serialize(snapshot.Simulation),
                    ["signal-bindings.json"] = Serialize(snapshot.SignalBindings)
                };
                var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in files)
                {
                    await WriteFileAsync(Path.Combine(stagingDirectory, pair.Key), pair.Value, ct);
                    hashes[pair.Key] = Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant();
                }

                var manifest = new RevisionManifest
                {
                    SchemaVersion = ManifestSchemaVersion,
                    Revision = snapshot.Revision,
                    Files = hashes
                };
                await WriteFileAsync(Path.Combine(stagingDirectory, "manifest.json"), Serialize(manifest), ct);
                Directory.Move(stagingDirectory, finalDirectory);
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CommitActiveAsync(string revision, CancellationToken ct = default)
    {
        ValidateRevision(revision);
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var manifest = await ReadManifestAsync(revision, ct);
            var pointer = new ActivePointer
            {
                SchemaVersion = ManifestSchemaVersion,
                Revision = manifest.Revision,
                ManifestSha256 = HashFile(ManifestPath(revision)),
                CommittedAtUtc = DateTime.UtcNow
            };
            Directory.CreateDirectory(_root);
            var activePath = Path.Combine(_root, ActiveFileName);
            var previousPath = Path.Combine(_root, PreviousActiveFileName);
            if (File.Exists(activePath)) File.Copy(activePath, previousPath, overwrite: true);

            var tempPath = Path.Combine(_root, ".active-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await WriteFileAsync(tempPath, Serialize(pointer), ct);
                File.Move(tempPath, activePath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<DeviceConfigurationSnapshot> LoadRevisionCoreAsync(
        string revision,
        CancellationToken ct,
        bool validateSemantics = true)
    {
        ValidateRevision(revision);
        var manifest = await ReadManifestAsync(revision, ct);
        foreach (var pair in manifest.Files)
        {
            if (!RequiredRevisionFiles.Contains(pair.Key)
                || Path.IsPathRooted(pair.Key)
                || pair.Key.Contains('/')
                || pair.Key.Contains('\\'))
                throw new ConfigValidationException($"revision {revision} 清单包含非法文件名：{pair.Key}");
            if (pair.Value.Length != 64 || pair.Value.Any(character => !Uri.IsHexDigit(character)))
                throw new ConfigValidationException($"revision {revision} 文件哈希格式非法：{pair.Key}");
            var path = Path.Combine(RevisionDirectory(revision), pair.Key);
            if (!File.Exists(path)) throw new ConfigValidationException($"revision {revision} 缺少文件：{pair.Key}");
            var actual = HashFile(path);
            if (!string.Equals(actual, pair.Value, StringComparison.OrdinalIgnoreCase))
                throw new ConfigValidationException($"revision {revision} 文件哈希不匹配：{pair.Key}");
        }

        var directory = RevisionDirectory(revision);
        var snapshot = new DeviceConfigurationSnapshot
        {
            Revision = manifest.Revision,
            Device = await DeserializeAsync<DeviceConfig>(Path.Combine(directory, "device.json"), ct),
            Points = await DeserializeAsync<PointsConfig>(Path.Combine(directory, "points.json"), ct),
            Simulation = await DeserializeAsync<SimulationConfig>(Path.Combine(directory, "simulation.json"), ct),
            SignalBindings = await DeserializeAsync<SignalBindingsConfig>(Path.Combine(directory, "signal-bindings.json"), ct)
        };
        if (validateSemantics)
            MultiDeviceConfigurationValidator.EnsureValid(snapshot);
        return snapshot;
    }

    private async Task<RevisionManifest> ReadManifestAsync(string revision, CancellationToken ct)
    {
        var manifest = await DeserializeAsync<RevisionManifest>(ManifestPath(revision), ct)
            ?? throw new ConfigValidationException($"revision {revision} 清单为空");
        if (manifest.SchemaVersion != ManifestSchemaVersion)
            throw new ConfigValidationException($"revision {revision} 清单版本不受支持：{manifest.SchemaVersion}");
        if (!string.Equals(manifest.Revision, revision, StringComparison.Ordinal))
            throw new ConfigValidationException($"revision {revision} 清单身份不匹配：{manifest.Revision}");
        if (manifest.Files is null || manifest.Files.Count != RequiredRevisionFiles.Count
            || !RequiredRevisionFiles.SetEquals(manifest.Files.Keys))
            throw new ConfigValidationException($"revision {revision} 清单文件集合不完整");
        return manifest;
    }

    private async Task<ActivePointer> ReadPointerAsync(string pointerName, CancellationToken ct)
    {
        var path = Path.Combine(_root, pointerName);
        var pointer = await DeserializeAsync<ActivePointer>(path, ct);
        if (pointer.SchemaVersion != ManifestSchemaVersion || string.IsNullOrWhiteSpace(pointer.Revision))
            throw new ConfigValidationException($"生效指针无效：{pointerName}");
        if (pointer.ManifestSha256.Length != 64 || pointer.ManifestSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ConfigValidationException($"生效指针清单哈希格式非法：{pointerName}");
        var manifestHash = HashFile(ManifestPath(pointer.Revision));
        if (!string.Equals(manifestHash, pointer.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new ConfigValidationException($"生效指针清单哈希不匹配：{pointer.Revision}");
        return pointer;
    }

    private static async Task<T> DeserializeAsync<T>(string path, CancellationToken ct) where T : class
    {
        if (!File.Exists(path)) throw new ConfigValidationException($"配置文件不存在：{path}");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct)
            ?? throw new ConfigValidationException($"配置文件为空：{path}");
    }

    private static async Task WriteFileAsync(string path, byte[] content, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
            FileOptions.WriteThrough | FileOptions.Asynchronous);
        await stream.WriteAsync(content, ct);
        await stream.FlushAsync(ct);
        stream.Flush(flushToDisk: true);
    }

    private string RevisionDirectory(string revision) => Path.Combine(_revisionsRoot, revision);
    private string ManifestPath(string revision) => Path.Combine(RevisionDirectory(revision), "manifest.json");

    private static void ValidateRevision(string revision)
    {
        if (string.IsNullOrWhiteSpace(revision)
            || revision.Length > 128
            || revision.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new ConfigValidationException($"revision 非法：{revision}");
    }

    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed class RevisionManifest
    {
        public int SchemaVersion { get; set; }
        public string Revision { get; set; } = string.Empty;
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ActivePointer
    {
        public int SchemaVersion { get; set; }
        public string Revision { get; set; } = string.Empty;
        public string ManifestSha256 { get; set; } = string.Empty;
        public DateTime CommittedAtUtc { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
