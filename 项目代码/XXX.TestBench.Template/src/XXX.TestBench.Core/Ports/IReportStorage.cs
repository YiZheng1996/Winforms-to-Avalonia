using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 受控的报表版本存储。实现必须按 RevisionId 幂等发布，不覆盖既有版本。
/// </summary>
public interface IReportStorage
{
    Task<ReportStorageStage> StageAsync(
        Guid revisionId,
        string inputHash,
        IReadOnlyList<ReportArtifactContent> files,
        CancellationToken cancellationToken = default);

    Task<ReportStorageManifest> PublishAsync(
        ReportStorageManifest manifest,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task<StorageVerificationResult> VerifyAsync(
        string storageKey,
        string expectedSha256,
        CancellationToken cancellationToken = default);

    Task<ReportExportResult> ExportAsync(
        string storageKey,
        string targetPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 待归档文件内容。调用方负责在调用期间保持 Content 可读；存储实现负责完整读取并核对摘要。
/// </summary>
public sealed record ReportArtifactContent(
    string LogicalName,
    string MediaType,
    long Length,
    string Sha256,
    Stream Content);

public sealed record ReportStorageStage(
    Guid RevisionId,
    string InputHash,
    string StageKey,
    IReadOnlyList<ReportArtifactDescriptor> Artifacts);

public sealed record ReportArtifactDescriptor(
    string LogicalName,
    string StorageKey,
    string MediaType,
    long Length,
    string Sha256);

/// <summary>
/// 主 XLSX manifest 不包含后补 PDF 附件；PDF 由独立不可变附件合同维护。
/// </summary>
public sealed record ReportStorageManifest(
    int SchemaVersion,
    Guid RevisionId,
    string InputHash,
    string StageKey,
    IReadOnlyList<ReportArtifactDescriptor> Artifacts);

public sealed record StorageVerificationResult(bool Matches, string? ActualSha256, string? Error);

public sealed record ReportExportResult(bool Succeeded, string? Error);

/// <summary>
/// 报表 manifest 的唯一序列化合同；数据库保存的 ManifestHash 使用相同字节计算。
/// </summary>
public static class ReportStorageManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    public static byte[] Serialize(ReportStorageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }

    public static string ComputeSha256(ReportStorageManifest manifest)
        => Convert.ToHexString(SHA256.HashData(Serialize(manifest))).ToLowerInvariant();
}
