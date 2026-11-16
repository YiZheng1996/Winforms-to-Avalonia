namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 安装级配置，不进入可切换的业务数据库。
/// </summary>
public sealed record SystemSettings
{
    public const int CurrentSchemaVersion = 1;

    [System.Text.Json.Serialization.JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Revision { get; init; }
    public required string BenchId { get; init; }
    public required string NodeId { get; init; }
    public required string StateRoot { get; init; }
    public DatabaseActivationState DatabaseState { get; init; }
    public DatabaseProfile? ActiveProfile { get; init; }
    public DatabaseProfile? PendingProfile { get; init; }
    public DatabaseProfile? PreviousProfile { get; init; }
    public StorageProfile? Storage { get; init; }
    public string? ReportToolPath { get; init; }

    /// <summary>
    /// 检查结构与路径；不创建目录、不测试连接、不补造台架身份。
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != CurrentSchemaVersion)
            errors.Add($"不支持的 system-settings 版本：{SchemaVersion}");
        if (Revision < 0) errors.Add("Revision 不能小于零");
        if (string.IsNullOrWhiteSpace(BenchId)) errors.Add("BenchId 不能为空");
        if (!IsLowerGuid(NodeId)) errors.Add("NodeId 必须为小写 GUID D 格式");
        if (string.IsNullOrWhiteSpace(StateRoot) || !Path.IsPathFullyQualified(StateRoot))
            errors.Add("StateRoot 必须是已配置的绝对路径");
        if (!Enum.IsDefined(DatabaseState)) errors.Add("数据库激活状态未知");

        if (DatabaseState == DatabaseActivationState.Active && ActiveProfile is null)
            errors.Add("Active 状态必须包含 ActiveProfile");
        if (DatabaseState == DatabaseActivationState.Active
            && (ActiveProfile?.ExpectedDatabaseInstanceId is null || ActiveProfile.ExpectedStorageId is null))
            errors.Add("Active 配置必须固定 DatabaseInstanceId 和 StorageId");
        if (DatabaseState is DatabaseActivationState.Pending or DatabaseActivationState.Activating
            && PendingProfile is null)
            errors.Add($"{DatabaseState} 状态必须包含 PendingProfile");
        ValidateProfile(ActiveProfile, "ActiveProfile", errors);
        ValidateProfile(PendingProfile, "PendingProfile", errors);
        ValidateProfile(PreviousProfile, "PreviousProfile", errors);

        if (Storage is not null)
        {
            if (!IsLowerGuid(Storage.StorageId)) errors.Add("StorageId 必须为小写 GUID D 格式");
            ValidateAbsolutePath(Storage.ReportRoot, "Storage.ReportRoot", errors);
            ValidateAbsolutePath(Storage.ResourceRoot, "Storage.ResourceRoot", errors);
            ValidateAbsolutePath(Storage.EvidenceRoot, "Storage.EvidenceRoot", errors);
        }
        if (ReportToolPath is not null && !Path.IsPathFullyQualified(ReportToolPath))
            errors.Add("ReportToolPath 必须是绝对路径");
        return errors;
    }

    private static void ValidateProfile(DatabaseProfile? profile, string name, ICollection<string> errors)
    {
        if (profile is null) return;
        if (string.IsNullOrWhiteSpace(profile.ProfileId)) errors.Add($"{name}.ProfileId 不能为空");
        if (string.IsNullOrWhiteSpace(profile.ProviderKey)) errors.Add($"{name}.ProviderKey 不能为空");
        if (string.IsNullOrWhiteSpace(profile.DisplayName)) errors.Add($"{name}.DisplayName 不能为空");
        if (profile.Revision < 0) errors.Add($"{name}.Revision 不能小于零");
        if (profile.ConnectionTimeoutSeconds <= 0) errors.Add($"{name}.ConnectionTimeoutSeconds 必须大于零");
        if (profile.SqlitePath is not null) ValidateAbsolutePath(profile.SqlitePath, $"{name}.SqlitePath", errors);
        if (profile.Port is <= 0 or > 65535) errors.Add($"{name}.Port 超出有效端口范围");
        if (profile.ExpectedDatabaseInstanceId is not null && !IsLowerGuid(profile.ExpectedDatabaseInstanceId))
            errors.Add($"{name}.ExpectedDatabaseInstanceId 必须为小写 GUID D 格式");
        if (profile.ExpectedStorageId is not null && !IsLowerGuid(profile.ExpectedStorageId))
            errors.Add($"{name}.ExpectedStorageId 必须为小写 GUID D 格式");
    }

    private static void ValidateAbsolutePath(string path, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            errors.Add($"{field} 必须是绝对路径");
    }

    private static bool IsLowerGuid(string value)
        => Guid.TryParseExact(value, "D", out var parsed)
           && string.Equals(value, parsed.ToString("D").ToLowerInvariant(), StringComparison.Ordinal);
}

public enum DatabaseActivationState
{
    Active = 0,
    Pending = 1,
    Activating = 2
}

/// <summary>
/// Provider 配置字段；不保存连接串或密码，密码只通过 SecretRef 取得。
/// </summary>
public sealed record DatabaseProfile
{
    public required string ProfileId { get; init; }
    public long Revision { get; init; }
    public required string ProviderKey { get; init; }
    public required string DisplayName { get; init; }
    public string? SqlitePath { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? DatabaseName { get; init; }
    public string? UserName { get; init; }
    public string? SecretRef { get; init; }
    public string? TlsProfileKey { get; init; }
    public int ConnectionTimeoutSeconds { get; init; }
    public string? ExpectedDatabaseInstanceId { get; init; }
    public string? ExpectedStorageId { get; init; }
}

public sealed record StorageProfile(
    string StorageId,
    string ReportRoot,
    string ResourceRoot,
    string EvidenceRoot);
