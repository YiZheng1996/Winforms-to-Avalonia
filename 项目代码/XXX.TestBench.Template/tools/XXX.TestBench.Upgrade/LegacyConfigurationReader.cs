using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XXX.TestBench.Upgrade;

internal sealed record LegacyConfigurationInput(
    string SourceRoot,
    string SourceKind,
    string PointerSelection,
    IReadOnlyDictionary<string, byte[]> Files,
    IReadOnlyDictionary<string, string> FileHashes,
    bool HasSignalBindings,
    string CombinedHash);

/// <summary>Reads only the explicitly named configuration files or one explicitly selected revision.</summary>
internal static class LegacyConfigurationReader
{
    private static readonly string[] RequiredFiles =
    {
        "device.json", "points.json", "simulation.json", "signal-bindings.json"
    };

    private static readonly JsonSerializerOptions StrictJson = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<LegacyConfigurationInput> ReadAsync(
        string sourcePath,
        string sourceKind,
        string pointerSelection,
        CancellationToken ct)
    {
        sourceKind = sourceKind.Trim().ToLowerInvariant();
        pointerSelection = pointerSelection.Trim().ToLowerInvariant();
        if (!Path.IsPathFullyQualified(sourcePath))
            throw new UpgradeCommandException(2, "--source 必须是绝对路径");
        if (!Directory.Exists(sourcePath))
            throw new UpgradeCommandException(5, "配置来源目录不存在");

        var root = Path.GetFullPath(sourcePath);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var hasBindings = true;
        var selection = "files";

        if (string.Equals(sourceKind, "files", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(pointerSelection, "active", StringComparison.OrdinalIgnoreCase))
                throw new UpgradeCommandException(2, "--pointer 仅适用于 revision 模式");

            foreach (var name in RequiredFiles)
            {
                ct.ThrowIfCancellationRequested();
                var path = Path.Combine(root, name);
                if (!File.Exists(path))
                {
                    if (name == "signal-bindings.json")
                    {
                        hasBindings = false;
                        continue;
                    }
                    throw new UpgradeCommandException(2, $"独立文件模式缺少明确输入文件：{name}");
                }
                files.Add(name, await ReadFileAsync(path, ct));
            }

            if (!hasBindings)
                files.Add("signal-bindings.json", "{\"schemaVersion\":1,\"bindings\":{}}"u8.ToArray());
        }
        else if (string.Equals(sourceKind, "revision", StringComparison.OrdinalIgnoreCase))
        {
            if (pointerSelection is not ("active" or "previous"))
                throw new UpgradeCommandException(2, "--pointer 必须是 active 或 previous");
            selection = pointerSelection;
            var deviceConfigRoot = Path.Combine(root, "device-config");
            var pointerName = pointerSelection == "previous" ? "active.previous.json" : "active.json";
            var pointerPath = Path.Combine(deviceConfigRoot, pointerName);
            if (!File.Exists(pointerPath))
                throw new UpgradeCommandException(2, $"revision 模式缺少显式指针：{pointerName}");

            var pointerBytes = await ReadFileAsync(pointerPath, ct);
            var pointer = DeserializeStrict<ActivePointer>(pointerBytes, pointerPath);
            ValidateRevision(pointer.Revision);
            if (pointer.SchemaVersion != 1 || !IsSha256(pointer.ManifestSha256))
                throw new UpgradeCommandException(2, "revision 指针版本或 manifest 哈希格式无效");

            var revisionsRoot = Path.Combine(deviceConfigRoot, "revisions");
            var revisionDirectory = Path.GetFullPath(Path.Combine(revisionsRoot, pointer.Revision));
            EnsureContained(revisionsRoot, revisionDirectory, "revision 路径越界");
            if (!Directory.Exists(revisionDirectory))
                throw new UpgradeCommandException(2, $"revision 目录不存在：{pointer.Revision}");
            var resolvedRevisionDirectory = ResolveExisting(revisionDirectory);
            EnsureContained(ResolveExisting(revisionsRoot), resolvedRevisionDirectory, "revision 目录符号链接指向允许范围外");

            var manifestPath = Path.Combine(revisionDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new UpgradeCommandException(2, "revision 缺少 manifest.json");
            var manifestBytes = await ReadFileAsync(manifestPath, ct);
            var manifestHash = Hash(manifestBytes);
            if (!string.Equals(manifestHash, pointer.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new UpgradeCommandException(4, "revision 指针中的 manifest 哈希不匹配");
            var manifest = DeserializeStrict<RevisionManifest>(manifestBytes, manifestPath);
            if (manifest.SchemaVersion != 1
                || !string.Equals(manifest.Revision, pointer.Revision, StringComparison.Ordinal)
                || manifest.Files is null
                || manifest.Files.Count != RequiredFiles.Length
                || !RequiredFiles.ToHashSet(StringComparer.Ordinal).SetEquals(manifest.Files.Keys))
                throw new UpgradeCommandException(2, "manifest 版本、revision 或文件集合不符合 v1 合同");

            var actualEntries = Directory.EnumerateFileSystemEntries(revisionDirectory)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            var expectedEntries = RequiredFiles.Append("manifest.json").ToHashSet(StringComparer.Ordinal);
            if (!actualEntries.SetEquals(expectedEntries))
                throw new UpgradeCommandException(2, "revision 目录文件集合不是四个配置文件加 manifest.json");

            foreach (var name in RequiredFiles)
            {
                ct.ThrowIfCancellationRequested();
                var expectedHash = manifest.Files[name];
                if (!IsSha256(expectedHash))
                    throw new UpgradeCommandException(2, $"manifest 中 {name} 的 SHA-256 格式无效");
                var path = Path.Combine(revisionDirectory, name);
                var bytes = await ReadFileAsync(path, ct);
                var actualHash = Hash(bytes);
                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                    throw new UpgradeCommandException(4, $"revision 文件哈希不匹配：{name}");
                files.Add(name, bytes);
            }
        }
        else
        {
            throw new UpgradeCommandException(2, "--source-kind 必须是 revision 或 files");
        }

        var sourceFiles = hasBindings
            ? files
            : files.Where(pair => pair.Key != "signal-bindings.json").ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in sourceFiles)
            hashes[pair.Key] = Hash(pair.Value);
        return new LegacyConfigurationInput(root, sourceKind.ToLowerInvariant(), selection, files, hashes,
            hasBindings, HashFileSet(sourceFiles));
    }

    public static async Task<IReadOnlyDictionary<string, string>> RehashSourceAsync(
        LegacyConfigurationInput input,
        string pointerSelection,
        CancellationToken ct)
    {
        var reread = await ReadAsync(input.SourceRoot, input.SourceKind, pointerSelection, ct);
        return reread.FileHashes;
    }

    private static async Task<byte[]> ReadFileAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (IOException ex)
        {
            throw new UpgradeCommandException(5, $"读取配置文件失败或文件被占用：{path}：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UpgradeCommandException(5, $"无权读取配置文件：{path}：{ex.Message}");
        }
    }

    private static T DeserializeStrict<T>(byte[] bytes, string path) where T : class
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            EnsureNoDuplicateProperties(document.RootElement, path);
            return JsonSerializer.Deserialize<T>(bytes, StrictJson)
                ?? throw new UpgradeCommandException(2, $"JSON 根对象为空：{path}");
        }
        catch (JsonException ex)
        {
            throw new UpgradeCommandException(2, $"JSON 格式无效或含未知字段：{path}：{ex.Message}");
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new UpgradeCommandException(2, $"{path} 存在重复 JSON 字段：{property.Name}");
                EnsureNoDuplicateProperties(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                EnsureNoDuplicateProperties(item, path + $"[{index++}]");
        }
    }

    private static string HashFileSet(IReadOnlyDictionary<string, byte[]> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(pair.Key));
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(pair.Value);
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void ValidateRevision(string? revision)
    {
        if (string.IsNullOrWhiteSpace(revision)
            || revision.Length > 128
            || revision is "." or ".."
            || revision.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new UpgradeCommandException(2, "revision 名称为空、含路径字符或格式非法");
    }

    private static string ResolveExisting(string path)
    {
        var info = new DirectoryInfo(Path.GetFullPath(path));
        return info.ResolveLinkTarget(true)?.FullName ?? info.FullName;
    }

    private static void EnsureContained(string root, string child, string message)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullChild = Path.GetFullPath(child);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullChild.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UpgradeCommandException(2, message);
    }

    private sealed class ActivePointer
    {
        public int SchemaVersion { get; set; }
        public string Revision { get; set; } = string.Empty;
        public string ManifestSha256 { get; set; } = string.Empty;
        public DateTime CommittedAtUtc { get; set; }
    }

    private sealed class RevisionManifest
    {
        public int SchemaVersion { get; set; }
        public string Revision { get; set; } = string.Empty;
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
    }
}
