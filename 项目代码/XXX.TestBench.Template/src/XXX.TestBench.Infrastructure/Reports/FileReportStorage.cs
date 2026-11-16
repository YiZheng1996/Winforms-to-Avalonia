using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Reports;

/// <summary>
/// 本地报表文件存储。先校验独立暂存文件，再原子移动到 RevisionId 专属目录并发布 manifest。
/// </summary>
public sealed class FileReportStorage : IReportStorage
{
    private sealed record PublicationIntent(
        int SchemaVersion,
        Guid RevisionId,
        string InputHash,
        string StageKey,
        IReadOnlyList<ReportArtifactDescriptor> Artifacts);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    private readonly string _root;
    private readonly string _rootPrefix;
    private readonly StringComparison _pathComparison;

    public FileReportStorage(string reportRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportRoot);
        if (!Path.IsPathFullyQualified(reportRoot))
            throw new ArgumentException("报表存储根目录必须是绝对路径。", nameof(reportRoot));

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(reportRoot));
        if (string.Equals(_root, Path.GetPathRoot(_root), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
            throw new ArgumentException("报表存储根目录必须是专用目录，不能直接使用磁盘根目录。", nameof(reportRoot));
        _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _rootPrefix = _root + Path.DirectorySeparatorChar;
    }

    public async Task<ReportStorageStage> StageAsync(
        Guid revisionId,
        string inputHash,
        IReadOnlyList<ReportArtifactContent> files,
        CancellationToken cancellationToken = default)
    {
        if (revisionId == Guid.Empty) throw new ArgumentException("RevisionId 不能为空。", nameof(revisionId));
        EnsureHash(inputHash, nameof(inputHash));
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0) throw new InvalidDataException("报表暂存至少需要一个产物文件。");

        var revisionKey = revisionId.ToString("D").ToLowerInvariant();
        var stageKey = $".staging/{revisionKey}/{Guid.NewGuid():N}";
        var stageDirectory = ResolveStageDirectory(stageKey, revisionId);
        Directory.CreateDirectory(stageDirectory);

        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var artifacts = new List<ReportArtifactDescriptor>(files.Count);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateArtifact(file);
                if (!names.Add(file.LogicalName))
                    throw new InvalidDataException($"报表产物文件名重复：{file.LogicalName}");

                var destination = Path.Combine(stageDirectory, file.LogicalName);
                EnsureNoReparsePoints(destination);
                await WriteAndVerifyAsync(file, destination, cancellationToken);
                artifacts.Add(new ReportArtifactDescriptor(
                    file.LogicalName,
                    CreateReportKey(revisionId, file.LogicalName),
                    file.MediaType,
                    file.Length,
                    file.Sha256));
            }

            await WritePublicationIntentAsync(
                stageDirectory,
                new PublicationIntent(1, revisionId, inputHash, stageKey, artifacts),
                cancellationToken);

            return new ReportStorageStage(revisionId, inputHash, stageKey, artifacts);
        }
        catch
        {
            DeleteOwnedStageDirectory(stageDirectory);
            throw;
        }
    }

    public async Task<ReportStorageManifest> PublishAsync(
        ReportStorageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateManifest(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        var finalDirectory = ResolveReportDirectory(manifest.RevisionId);
        var manifestPath = Path.Combine(finalDirectory, "manifest.json");
        EnsureNoReparsePoints(manifestPath);
        if (File.Exists(manifestPath))
        {
            var existing = await ReadManifestAsync(manifestPath, cancellationToken);
            EnsureSameManifest(existing, manifest);
            await VerifyArtifactsAsync(finalDirectory, manifest.Artifacts, cancellationToken);
            return existing;
        }

        var stageDirectory = ResolveStageDirectory(manifest.StageKey, manifest.RevisionId);
        if (Directory.Exists(finalDirectory))
        {
            // 恢复“文件目录已移动、manifest 尚未发布”的中断窗口；先核对目录内的发布意图。
            await EnsurePublicationIntentMatchesAsync(finalDirectory, manifest, cancellationToken);
            await VerifyArtifactsAsync(finalDirectory, manifest.Artifacts, cancellationToken);
        }
        else
        {
            if (!Directory.Exists(stageDirectory))
                throw new DirectoryNotFoundException("报表暂存目录不存在，不能发布 manifest。");
            await EnsurePublicationIntentMatchesAsync(stageDirectory, manifest, cancellationToken);
            await VerifyArtifactsAsync(stageDirectory, manifest.Artifacts, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
            EnsureNoReparsePoints(finalDirectory);
            try
            {
                Directory.Move(stageDirectory, finalDirectory);
            }
            catch (IOException) when (Directory.Exists(finalDirectory))
            {
                // 并发发布时只接受另一请求已写入完全相同的发布意图与不可变产物。
                await EnsurePublicationIntentMatchesAsync(finalDirectory, manifest, cancellationToken);
                await VerifyArtifactsAsync(finalDirectory, manifest.Artifacts, cancellationToken);
            }
        }

        await VerifyArtifactsAsync(finalDirectory, manifest.Artifacts, cancellationToken);
        await WriteManifestAtomicallyAsync(manifestPath, manifest, cancellationToken);
        return await ReadManifestAsync(manifestPath, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveReportArtifactPath(storageKey);
        EnsureNoReparsePoints(path);
        var parts = storageKey.Split('/');
        var revisionId = Guid.ParseExact(parts[1], "D");
        var manifest = await ReadManifestAsync(
            Path.Combine(ResolveReportDirectory(revisionId), "manifest.json"), cancellationToken);
        var artifact = manifest.Artifacts.SingleOrDefault(item =>
            string.Equals(item.StorageKey, storageKey, StringComparison.Ordinal));
        if (artifact is null)
            throw new InvalidDataException("StorageKey 不属于该 RevisionId 的已发布 manifest。");

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (stream.Length != artifact.Length)
                throw new InvalidDataException($"报表产物长度与 manifest 不一致：{artifact.LogicalName}");
            var digest = await SHA256.HashDataAsync(stream, cancellationToken);
            var actual = Convert.ToHexString(digest).ToLowerInvariant();
            if (!string.Equals(actual, artifact.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"报表产物摘要与 manifest 不一致：{artifact.LogicalName}");
            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    public async Task<StorageVerificationResult> VerifyAsync(
        string storageKey,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureHash(expectedSha256, nameof(expectedSha256));
            await using var stream = await OpenReadAsync(storageKey, cancellationToken);
            var digest = await SHA256.HashDataAsync(stream, cancellationToken);
            var actual = Convert.ToHexString(digest).ToLowerInvariant();
            return new StorageVerificationResult(
                string.Equals(actual, expectedSha256, StringComparison.Ordinal),
                actual,
                string.Equals(actual, expectedSha256, StringComparison.Ordinal) ? null : "文件 SHA-256 与 manifest 不一致。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            return new StorageVerificationResult(false, null, ex.Message);
        }
    }

    public async Task<ReportExportResult> ExportAsync(
        string storageKey,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        string? temporaryPath = null;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
            if (!Path.IsPathFullyQualified(targetPath))
                throw new ArgumentException("导出目标必须是绝对路径。", nameof(targetPath));

            var fullTargetPath = Path.GetFullPath(targetPath);
            var targetDirectory = Path.GetDirectoryName(fullTargetPath)
                ?? throw new ArgumentException("导出目标缺少目录。", nameof(targetPath));
            if (!Directory.Exists(targetDirectory))
                throw new DirectoryNotFoundException("导出目标目录不存在。");
            if (File.Exists(fullTargetPath))
                throw new IOException("导出目标文件已存在；为避免覆盖用户文件，请选择新文件名。");

            temporaryPath = fullTargetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using var source = await OpenReadAsync(storageKey, cancellationToken);
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullTargetPath, overwrite: false);
            temporaryPath = null;
            return new ReportExportResult(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            return new ReportExportResult(false, ex.Message);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void ValidateArtifact(ReportArtifactContent file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Content is null || file.Length < 0 || string.IsNullOrWhiteSpace(file.MediaType))
            throw new InvalidDataException("报表产物缺少内容流、有效长度或媒体类型。");
        EnsureLogicalFileName(file.LogicalName);
        EnsureHash(file.Sha256, nameof(file.Sha256));
    }

    private static void ValidateManifest(ReportStorageManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.RevisionId == Guid.Empty)
            throw new InvalidDataException("报表 manifest 版本或 RevisionId 无效。");
        EnsureHash(manifest.InputHash, nameof(manifest.InputHash));
        if (manifest.Artifacts is null || manifest.Artifacts.Count == 0)
            throw new InvalidDataException("报表 manifest 缺少产物。");

        _ = ParseStageKey(manifest.StageKey, manifest.RevisionId);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in manifest.Artifacts)
        {
            if (artifact is null || string.IsNullOrWhiteSpace(artifact.MediaType) || artifact.Length < 0)
                throw new InvalidDataException("报表 manifest 包含无效产物描述。");
            EnsureLogicalFileName(artifact.LogicalName);
            EnsureHash(artifact.Sha256, nameof(artifact.Sha256));
            if (!names.Add(artifact.LogicalName)
                || !string.Equals(artifact.StorageKey,
                    CreateReportKey(manifest.RevisionId, artifact.LogicalName), StringComparison.Ordinal))
                throw new InvalidDataException("报表 manifest 包含重复文件名或越界 StorageKey。");
        }
    }

    private async Task VerifyArtifactsAsync(
        string directory,
        IReadOnlyList<ReportArtifactDescriptor> artifacts,
        CancellationToken cancellationToken)
    {
        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureLogicalFileName(artifact.LogicalName);
            var path = Path.Combine(directory, artifact.LogicalName);
            EnsureNoReparsePoints(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != artifact.Length)
                throw new InvalidDataException($"报表产物缺失或长度不符：{artifact.LogicalName}");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(stream, cancellationToken);
            var actual = Convert.ToHexString(digest).ToLowerInvariant();
            if (!string.Equals(actual, artifact.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"报表产物摘要不符：{artifact.LogicalName}");
        }
    }

    private async Task WritePublicationIntentAsync(
        string stageDirectory,
        PublicationIntent intent,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(stageDirectory, "publication-intent.json");
        EnsureNoReparsePoints(path);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(intent, JsonOptions);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private async Task EnsurePublicationIntentMatchesAsync(
        string directory,
        ReportStorageManifest manifest,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "publication-intent.json");
        EnsureNoReparsePoints(path);
        if (!File.Exists(path))
            throw new InvalidDataException("报表目录缺少发布意图，不能建立或补写 manifest。");

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        PublicationIntent intent;
        try
        {
            intent = await JsonSerializer.DeserializeAsync<PublicationIntent>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("报表发布意图为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("报表发布意图格式无效。", ex);
        }
        var intentManifest = new ReportStorageManifest(
            intent.SchemaVersion, intent.RevisionId, intent.InputHash, intent.StageKey, intent.Artifacts);
        ValidateManifest(intentManifest);
        if (!string.Equals(intent.StageKey, manifest.StageKey, StringComparison.Ordinal)
            || intent.RevisionId != manifest.RevisionId
            || !string.Equals(intent.InputHash, manifest.InputHash, StringComparison.Ordinal)
            || !intent.Artifacts.OrderBy(item => item.LogicalName, StringComparer.Ordinal)
                .SequenceEqual(manifest.Artifacts.OrderBy(item => item.LogicalName, StringComparer.Ordinal)))
            throw new IOException("报表发布意图与请求的输入、尝试或产物不一致；拒绝关联。");
    }

    private async Task WriteAndVerifyAsync(
        ReportArtifactContent artifact,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        await using (var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write,
                         FileShare.None, buffer.Length, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            while (true)
            {
                var read = await artifact.Content.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;
                total = checked(total + read);
                if (total > artifact.Length)
                    throw new InvalidDataException($"报表产物长度超过声明值：{artifact.LogicalName}");
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != artifact.Length || !string.Equals(actual, artifact.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException($"报表产物长度或摘要不符：{artifact.LogicalName}");
    }

    private async Task WriteManifestAtomicallyAsync(
        string manifestPath,
        ReportStorageManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        EnsureNoReparsePoints(manifestPath);
        var bytes = ReportStorageManifestSerializer.Serialize(manifest);
        var temporaryPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryPath, manifestPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(manifestPath))
            {
                var existing = await ReadManifestAsync(manifestPath, cancellationToken);
                EnsureSameManifest(existing, manifest);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task<ReportStorageManifest> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        ReportStorageManifest manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<ReportStorageManifest>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("报表 manifest 为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("报表 manifest 格式无效。", ex);
        }
        ValidateManifest(manifest);
        return manifest;
    }

    private static void EnsureSameManifest(ReportStorageManifest existing, ReportStorageManifest requested)
    {
        var sameArtifacts = existing.Artifacts.OrderBy(item => item.LogicalName, StringComparer.Ordinal)
            .SequenceEqual(requested.Artifacts.OrderBy(item => item.LogicalName, StringComparer.Ordinal));
        if (existing.SchemaVersion != requested.SchemaVersion
            || existing.RevisionId != requested.RevisionId
            || !string.Equals(existing.InputHash, requested.InputHash, StringComparison.Ordinal)
            || !sameArtifacts)
            throw new IOException("同一 RevisionId 已发布不同输入或产物；不可变报表版本不能覆盖。");
    }

    private string ResolveStageDirectory(string stageKey, Guid revisionId)
    {
        var stageId = ParseStageKey(stageKey, revisionId);
        return ResolveRootedPath(Path.Combine(".staging", revisionId.ToString("D").ToLowerInvariant(), stageId));
    }

    private string ResolveReportDirectory(Guid revisionId)
        => ResolveRootedPath(revisionId.ToString("D").ToLowerInvariant());

    private string ResolveReportArtifactPath(string storageKey)
    {
        var parts = storageKey?.Split('/') ?? Array.Empty<string>();
        if (parts.Length != 3 || !string.Equals(parts[0], "reports", StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[1], "D", out var revisionId)
            || !string.Equals(parts[1], revisionId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException("StorageKey 必须是受控 reports/{RevisionId}/{文件名} 格式。");
        EnsureLogicalFileName(parts[2]);
        return ResolveRootedPath(Path.Combine(parts[1], parts[2]));
    }

    private string ResolveRootedPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("报表存储相对路径不能是绝对路径。");
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_rootPrefix, _pathComparison))
            throw new InvalidDataException("报表存储路径越出配置根目录。");
        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    private void EnsureNoReparsePoints(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(_rootPrefix, _pathComparison)
            && !string.Equals(fullPath, _root, _pathComparison))
            throw new InvalidDataException("报表路径越出配置根目录。");

        var relative = Path.GetRelativePath(_root, fullPath);
        var current = _root;
        if (Directory.Exists(current)
            && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("报表存储根目录不能是重解析点。");

        if (relative == ".") return;
        foreach (var part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("报表存储路径不能经过重解析点。");
        }
    }

    private static string ParseStageKey(string? stageKey, Guid revisionId)
    {
        var parts = stageKey?.Split('/') ?? Array.Empty<string>();
        if (parts.Length != 3 || !string.Equals(parts[0], ".staging", StringComparison.Ordinal)
            || !string.Equals(parts[1], revisionId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal)
            || parts[2].Length != 32 || parts[2].Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("StageKey 必须是受控 .staging/{RevisionId}/{StageId} 格式。");
        return parts[2].ToLowerInvariant();
    }

    private static string CreateReportKey(Guid revisionId, string logicalName)
        => $"reports/{revisionId:D}/{logicalName}";

    private static void EnsureLogicalFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128
            || name is "." or ".."
            || string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "publication-intent.json", StringComparison.OrdinalIgnoreCase)
            || name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            || name.EndsWith(".", StringComparison.Ordinal))
            throw new InvalidDataException("报表产物逻辑名必须是安全的单级文件名，且不能占用 manifest.json。");
    }

    private static void EnsureHash(string? value, string field)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character))
            || value.Any(char.IsUpper))
            throw new InvalidDataException($"{field} 必须为 64 位小写 SHA-256。");
    }

    private static void DeleteOwnedStageDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { /* 保留未能清理的暂存目录，恢复扫描不会把它当作已发布报表。 */ }
        catch (UnauthorizedAccessException) { /* 清理失败不覆盖原始写入异常。 */ }
    }
}
