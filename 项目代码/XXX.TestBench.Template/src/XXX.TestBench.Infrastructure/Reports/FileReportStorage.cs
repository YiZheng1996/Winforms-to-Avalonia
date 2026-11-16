using System.Security.Cryptography;
using System.Text.Json;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Reports;

/// <summary>
/// 将报表产物先写入暂存目录，再以不可变版本目录和 manifest 发布。
/// </summary>
public sealed class FileReportStorage : IReportStorage, IReportArchiveReader
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly string _rootPath;

    /// <summary>读取并完整校验已发布版本，不把未发布的暂存目录视为成功。</summary>
    public async Task<ReportStorageManifest?> ReadPublishedAsync(Guid revisionId, CancellationToken ct = default)
    {
        if (revisionId == Guid.Empty) throw new ArgumentException("报表版本不能为空。", nameof(revisionId));
        var manifest = await ReadManifestIfExistsAsync(
            ResolvePath($"versions/{revisionId:D}/manifest.json"), ct);
        if (manifest is null) return null;
        ValidateManifest(manifest);
        if (manifest.RevisionId != revisionId) throw new InvalidDataException("归档版本身份不匹配。");
        await VerifyManifestArtifactsAsync(manifest, ct);
        return manifest;
    }

    /// <summary>
    /// 创建 StateRoot 下的 reports 存储。
    /// </summary>
    public FileReportStorage(string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        if (!Path.IsPathFullyQualified(stateRoot))
            throw new ArgumentException("StateRoot 必须是绝对路径。", nameof(stateRoot));

        _rootPath = Path.Combine(Path.GetFullPath(stateRoot), "reports");
    }

    /// <summary>
    /// 将输入文件写入独立暂存目录，并核对长度和 SHA-256。
    /// </summary>
    public async Task<ReportStorageStage> StageAsync(
        Guid revisionId,
        string inputHash,
        IReadOnlyList<ReportArtifactContent> files,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(revisionId, inputHash);
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
            throw new InvalidDataException("报表归档至少需要一个文件。");

        var stageId = Guid.NewGuid().ToString("N");
        var stageKey = $"staging/{revisionId:D}/{inputHash}/{stageId}";
        var stagePath = ResolvePath(stageKey);
        var temporaryPath = stagePath + ".tmp";
        var names = new HashSet<string>(StringComparer.Ordinal);
        var descriptors = new List<ReportArtifactDescriptor>(files.Count);

        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath)!);
        Directory.CreateDirectory(temporaryPath);
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateArtifactContent(file);
                if (!names.Add(file.LogicalName))
                    throw new InvalidDataException($"报表归档存在重复文件名：{file.LogicalName}");

                var filePath = Path.Combine(temporaryPath, file.LogicalName);
                var (length, hash) = await CopyAndHashAsync(file, filePath, cancellationToken);
                if (length != file.Length || !string.Equals(hash, file.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException($"报表暂存文件与声明的摘要不一致：{file.LogicalName}");

                descriptors.Add(new ReportArtifactDescriptor(
                    file.LogicalName,
                    $"versions/{revisionId:D}/{stageId}/{file.LogicalName}",
                    file.MediaType,
                    length,
                    hash));
            }

            Directory.Move(temporaryPath, stagePath);
            return new ReportStorageStage(revisionId, inputHash, stageKey, descriptors);
        }
        catch
        {
            if (Directory.Exists(temporaryPath))
                Directory.Delete(temporaryPath, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// 原子发布文件目录和 manifest；同一 RevisionId 不接受不同 manifest。
    /// </summary>
    public async Task<ReportStorageManifest> PublishAsync(
        ReportStorageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateManifest(manifest);

        var manifestPath = ResolvePath($"versions/{manifest.RevisionId:D}/manifest.json");
        var existing = await ReadManifestIfExistsAsync(manifestPath, cancellationToken);
        if (existing is not null)
        {
            EnsureSameManifest(existing, manifest);
            await VerifyManifestArtifactsAsync(existing, cancellationToken);
            return existing;
        }

        var (stageId, stagePath) = GetStageLocation(manifest);
        var finalDirectory = ResolvePath($"versions/{manifest.RevisionId:D}/{stageId}");
        if (Directory.Exists(stagePath))
        {
            await VerifyDirectoryArtifactsAsync(stagePath, manifest.Artifacts, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
            if (Directory.Exists(finalDirectory))
            {
                await VerifyDirectoryArtifactsAsync(finalDirectory, manifest.Artifacts, cancellationToken);
            }
            else
            {
                try
                {
                    Directory.Move(stagePath, finalDirectory);
                }
                catch (IOException) when (Directory.Exists(finalDirectory))
                {
                    // 并发发布时，另一个调用可能已先完成目录改名。
                    await VerifyDirectoryArtifactsAsync(finalDirectory, manifest.Artifacts, cancellationToken);
                }
            }
        }
        else if (Directory.Exists(finalDirectory))
        {
            await VerifyDirectoryArtifactsAsync(finalDirectory, manifest.Artifacts, cancellationToken);
        }
        else
        {
            throw new DirectoryNotFoundException("报表暂存目录和待发布目录均不存在。");
        }

        await VerifyManifestArtifactsAsync(manifest, cancellationToken);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
        await PublishManifestFileAsync(manifestPath, manifestBytes, cancellationToken);

        var published = await ReadManifestIfExistsAsync(manifestPath, cancellationToken)
            ?? throw new IOException("报表 manifest 发布后无法读取。");
        EnsureSameManifest(published, manifest);
        await VerifyManifestArtifactsAsync(published, cancellationToken);
        return published;
    }

    /// <summary>
    /// 只允许读取已发布版本中的报表文件。
    /// </summary>
    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateStorageKey(storageKey);

        var revisionId = Guid.ParseExact(storageKey.Split('/')[1], "D");
        var manifestPath = ResolvePath($"versions/{revisionId:D}/manifest.json");
        var manifest = await ReadManifestIfExistsAsync(manifestPath, cancellationToken)
            ?? throw new FileNotFoundException("报表版本尚未发布。", manifestPath);
        if (!manifest.Artifacts.Any(artifact => string.Equals(artifact.StorageKey, storageKey, StringComparison.Ordinal)))
            throw new InvalidDataException("报表文件未列入已发布 manifest。");

        var path = ResolvePath(storageKey);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <summary>
    /// 按摘要校验已发布文件。
    /// </summary>
    public async Task<StorageVerificationResult> VerifyAsync(
        string storageKey,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if (!IsSha256(expectedSha256))
            return new StorageVerificationResult(false, null, "期望摘要格式无效。");

        try
        {
            await using var stream = await OpenReadAsync(storageKey, cancellationToken);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            return new StorageVerificationResult(
                string.Equals(actualHash, expectedSha256, StringComparison.Ordinal),
                actualHash,
                string.Equals(actualHash, expectedSha256, StringComparison.Ordinal) ? null : "文件摘要不匹配。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new StorageVerificationResult(false, null, ex.Message);
        }
    }

    /// <summary>
    /// 将已归档文件复制到用户指定的绝对路径，使用同目录临时文件替换目标。
    /// </summary>
    public async Task<ReportExportResult> ExportAsync(
        string storageKey,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        string? temporaryPath = null;
        try
        {
            ValidateStorageKey(storageKey);
            if (string.IsNullOrWhiteSpace(targetPath) || !Path.IsPathFullyQualified(targetPath))
                throw new InvalidDataException("导出目标必须是绝对路径。");

            var fullTargetPath = Path.GetFullPath(targetPath);
            var targetDirectory = Path.GetDirectoryName(fullTargetPath)
                ?? throw new InvalidDataException("导出目标缺少父目录。");
            Directory.CreateDirectory(targetDirectory);
            temporaryPath = Path.Combine(targetDirectory, $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");

            await using var source = await OpenReadAsync(storageKey, cancellationToken);
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullTargetPath, overwrite: true);
            return new ReportExportResult(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or ArgumentException or NotSupportedException)
        {
            return new ReportExportResult(false, ex.Message);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // 导出结果已发布时，临时文件清理失败不改变导出状态。
                }
                catch (UnauthorizedAccessException)
                {
                    // 导出结果已发布时，临时文件清理失败不改变导出状态。
                }
            }
        }
    }

    private static async Task<(long Length, string Sha256)> CopyAndHashAsync(
        ReportArtifactContent artifact,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, buffer.Length, FileOptions.Asynchronous | FileOptions.WriteThrough);

        while (true)
        {
            var count = await artifact.Content.ReadAsync(buffer, cancellationToken);
            if (count == 0)
                break;

            length = checked(length + count);
            if (length > artifact.Length)
                throw new InvalidDataException($"报表文件超过声明长度：{artifact.LogicalName}");
            hash.AppendData(buffer, 0, count);
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
        return (length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private async Task VerifyDirectoryArtifactsAsync(
        string directory,
        IReadOnlyList<ReportArtifactDescriptor> artifacts,
        CancellationToken cancellationToken)
    {
        foreach (var artifact in artifacts)
        {
            var path = Path.Combine(directory, artifact.LogicalName);
            await VerifyFileAsync(path, artifact.Length, artifact.Sha256, cancellationToken);
        }
    }

    private async Task VerifyManifestArtifactsAsync(
        ReportStorageManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var artifact in manifest.Artifacts)
        {
            var path = ResolvePath(artifact.StorageKey);
            await VerifyFileAsync(path, artifact.Length, artifact.Sha256, cancellationToken);
        }
    }

    private static async Task VerifyFileAsync(
        string path,
        long expectedLength,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != expectedLength)
            throw new InvalidDataException($"归档文件长度不匹配：{Path.GetFileName(path)}");

        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"归档文件摘要不匹配：{Path.GetFileName(path)}");
    }

    private async Task<ReportStorageManifest?> ReadManifestIfExistsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var manifest = JsonSerializer.Deserialize<ReportStorageManifest>(bytes, ManifestJsonOptions)
            ?? throw new InvalidDataException("报表 manifest 内容为空或格式无效。");
        ValidateManifest(manifest);
        return manifest;
    }

    private async Task PublishManifestFileAsync(
        string manifestPath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var temporaryPath = manifestPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, manifestPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(manifestPath))
            {
                // 并发发布时，下方由调用者读取并比较已发布 manifest。
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private (string StageId, string StagePath) GetStageLocation(ReportStorageManifest manifest)
    {
        var parts = manifest.StageKey.Split('/');
        if (parts.Length != 4
            || parts[0] != "staging"
            || !string.Equals(parts[1], manifest.RevisionId.ToString("D"), StringComparison.Ordinal)
            || !string.Equals(parts[2], manifest.InputHash, StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[3], "N", out _))
            throw new InvalidDataException("报表暂存键与 manifest 身份不匹配。");

        return (parts[3], ResolvePath(manifest.StageKey));
    }

    private static void ValidateManifest(ReportStorageManifest manifest)
    {
        ValidateIdentity(manifest.RevisionId, manifest.InputHash);
        if (manifest.SchemaVersion != 1 || manifest.Artifacts is null || manifest.Artifacts.Count == 0)
            throw new InvalidDataException("报表 manifest 版本或文件清单无效。");

        var stageParts = manifest.StageKey?.Split('/') ?? Array.Empty<string>();
        if (stageParts.Length != 4
            || stageParts[0] != "staging"
            || !string.Equals(stageParts[1], manifest.RevisionId.ToString("D"), StringComparison.Ordinal)
            || !string.Equals(stageParts[2], manifest.InputHash, StringComparison.Ordinal)
            || !Guid.TryParseExact(stageParts[3], "N", out _))
            throw new InvalidDataException("报表 manifest 的暂存键无效。");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in manifest.Artifacts)
        {
            ValidateLogicalName(artifact.LogicalName);
            if (!names.Add(artifact.LogicalName)
                || artifact.Length <= 0
                || string.IsNullOrWhiteSpace(artifact.MediaType)
                || !IsSha256(artifact.Sha256)
                || !string.Equals(artifact.StorageKey,
                    $"versions/{manifest.RevisionId:D}/{stageParts[3]}/{artifact.LogicalName}",
                    StringComparison.Ordinal))
                throw new InvalidDataException($"报表 manifest 文件描述无效：{artifact.LogicalName}");
        }
    }

    private static void EnsureSameManifest(ReportStorageManifest actual, ReportStorageManifest expected)
    {
        if (actual.SchemaVersion != expected.SchemaVersion
            || actual.RevisionId != expected.RevisionId
            || !string.Equals(actual.InputHash, expected.InputHash, StringComparison.Ordinal)
            || !string.Equals(actual.StageKey, expected.StageKey, StringComparison.Ordinal)
            || actual.Artifacts.Count != expected.Artifacts.Count)
            throw new InvalidDataException("同一报表版本已经发布了不同的 manifest。");

        foreach (var artifact in expected.Artifacts)
        {
            if (!actual.Artifacts.Contains(artifact))
                throw new InvalidDataException($"已发布的报表文件清单与本次请求不同：{artifact.LogicalName}");
        }
    }

    private string ResolvePath(string relativeKey)
    {
        if (string.IsNullOrWhiteSpace(relativeKey) || relativeKey.Contains('\\')
            || Path.IsPathRooted(relativeKey))
            throw new InvalidDataException("报表存储键必须是规范相对路径。");

        var segments = relativeKey.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidDataException("报表存储键包含无效路径段。");

        var path = Path.GetFullPath(Path.Combine(_rootPath, Path.Combine(segments)));
        var rootPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_rootPath)) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(rootPrefix, comparison))
            throw new InvalidDataException("报表存储键越出了 StateRoot reports 目录。");
        return path;
    }

    private static void ValidateStorageKey(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains('\\'))
            throw new InvalidDataException("报表存储键格式无效。");

        var parts = storageKey.Split('/');
        if (parts.Length != 4 || parts[0] != "versions"
            || !Guid.TryParseExact(parts[1], "D", out var revisionId)
            || !string.Equals(parts[1], revisionId.ToString("D"), StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[2], "N", out var stageId)
            || !string.Equals(parts[2], stageId.ToString("N"), StringComparison.Ordinal))
            throw new InvalidDataException("报表存储键不是已发布版本文件键。");
        ValidateLogicalName(parts[3]);
    }

    private static void ValidateArtifactContent(ReportArtifactContent file)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(file.Content);
        ValidateLogicalName(file.LogicalName);
        if (file.Length <= 0 || !IsSha256(file.Sha256) || string.IsNullOrWhiteSpace(file.MediaType))
            throw new InvalidDataException($"报表文件描述无效：{file.LogicalName}");
    }

    private static void ValidateLogicalName(string? logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName)
            || logicalName is "." or ".."
            || logicalName.Any(character => !(character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-')))
            throw new InvalidDataException("报表逻辑文件名只能包含 ASCII 字母、数字、点、下划线和短横线。");
    }

    private static void ValidateIdentity(Guid revisionId, string? inputHash)
    {
        if (revisionId == Guid.Empty || !IsSha256(inputHash))
            throw new InvalidDataException("报表版本标识或输入摘要无效。");
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
