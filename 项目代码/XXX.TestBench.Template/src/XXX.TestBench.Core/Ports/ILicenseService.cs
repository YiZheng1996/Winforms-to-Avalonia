using XXX.TestBench.Core.Application;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 离线授权边界。授权状态不依赖业务数据库；运行许可绑定单次 RunId。
/// </summary>
public interface ILicenseService
{
    Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<LicenseImportValidation> ValidateImportAsync(
        ReadOnlyMemory<byte> licenseFile,
        CancellationToken cancellationToken = default);

    Task<LicenseImportResult> ImportAsync(
        UserContext actor,
        ReadOnlyMemory<byte> licenseFile,
        CancellationToken cancellationToken = default);

    Task<RunLicensePermit> AcquireRunPermitAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 授权载荷的版本化字段。验签对象是外层 Base64 解码得到的原始 UTF-8 字节。
/// </summary>
public sealed record OfflineLicensePayloadV1(
    string ProductId,
    string LicenseId,
    string CustomerName,
    string MachineBinding,
    DateTime NotBeforeUtc,
    DateTime? ExpiresAtUtc,
    DateTime IssuedAtUtc,
    string KeyId);

public sealed record OfflineLicenseEnvelopeV1(
    int SchemaVersion,
    string PayloadBase64,
    string SignatureBase64);

public enum LicenseStatusCode
{
    NotImported = 0,
    Valid = 1,
    NotYetValid = 2,
    Expired = 3,
    InvalidSignature = 4,
    MachineMismatch = 5,
    ProductMismatch = 6,
    UnsupportedVersion = 7,
    UnknownKey = 8
}

public sealed record LicenseStatus(
    LicenseStatusCode Code,
    string Message,
    string? LicenseId,
    DateTime? NotBeforeUtc,
    DateTime? ExpiresAtUtc)
{
    public bool CanStartNewRun => Code == LicenseStatusCode.Valid;
}

public sealed record LicenseImportValidation(
    bool IsValid,
    LicenseStatusCode StatusCode,
    string Message,
    string? LicenseId,
    DateTime? ExpiresAtUtc);

public sealed record LicenseImportResult(
    bool Imported,
    LicenseStatus Status,
    string? Error);

/// <summary>
/// 仅由 ILicenseService 在正式开始事务内签发，不应由 UI 或执行器构造/替换。
/// </summary>
public sealed record RunLicensePermit(
    Guid PermitId,
    Guid RunId,
    string LicenseId,
    string PayloadHash,
    DateTime AcquiredAtUtc,
    DateTime? ExpiresAtUtc);
