using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;

namespace XXX.TestBench.App.Composition;

/// <summary>
/// 应用启动和首次配置的受控入口。正式启动先锁定 StateRoot、检查恢复标记，再只读验证并打开已有数据库。
/// </summary>
public sealed class ApplicationBootstrapper
{
    private static readonly TimeSpan FileGateTimeout = TimeSpan.FromSeconds(5);
    private readonly string _stateRoot;

    public ApplicationBootstrapper(string? stateRoot = null)
    {
        _stateRoot = Path.GetFullPath(stateRoot ?? ResolveDefaultStateRoot());
        if (!Path.IsPathFullyQualified(_stateRoot))
            throw new ArgumentException("StateRoot 必须是绝对路径", nameof(stateRoot));
    }

    public string StateRoot => _stateRoot;

    /// <summary>
    /// 生产启动预检。缺少设置、存在未闭合操作、库身份不符或 Provider 未安装时均拒绝启用硬件。
    /// </summary>
    public async Task<ApplicationBootstrapResult> BootstrapExistingAsync(CancellationToken cancellationToken = default)
    {
        var processLock = InstallationProcessLock.Acquire(_stateRoot);
        DatabaseServices? database = null;
        try
        {
            var settings = await ReadSettingsAsync(cancellationToken);
            EnsureStateRootMatches(settings);
            var fences = await ReadFencesAsync(cancellationToken);
            if (fences.Count > 0)
            {
                var first = fences[0];
                throw new InvalidOperationException(
                    $"检测到 {fences.Count} 条未闭合操作标记（{first.OperationId:D}，{first.Phase}）；进入恢复受限状态，不会启用设备。请先执行受控恢复。");
            }
            if (settings.DatabaseState != DatabaseActivationState.Active || settings.ActiveProfile is null)
                throw new InvalidOperationException("数据库配置处于待生效状态；启动不会自动切换或回退数据库。");
            if (settings.Storage is null
                || !string.Equals(settings.Storage.StorageId, settings.ActiveProfile.ExpectedStorageId, StringComparison.Ordinal))
                throw new InvalidDataException("活动数据库配置与安装级 StorageId 不一致。");

            var provider = CreateSqliteProvider();
            var test = await provider.TestAsync(settings.ActiveProfile, cancellationToken);
            if (!test.Succeeded)
                throw new InvalidOperationException(test.Message);
            database = await provider.OpenExistingAsync(settings.ActiveProfile, cancellationToken);
            return new ApplicationBootstrapResult(settings, database, processLock);
        }
        catch
        {
            database?.Dispose();
            processLock.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 首次安装的显式新库流程。路径必须不存在，台架身份和初始管理员密码由受控设置界面提供。
    /// </summary>
    public async Task<SystemSettings> InitializeNewInstallationAsync(
        string benchId,
        string databasePath,
        string storageRoot,
        string initialAdministratorPassword,
        CancellationToken cancellationToken = default)
    {
        ValidateSetupInputs(benchId, databasePath, storageRoot, initialAdministratorPassword);
        using var processLock = InstallationProcessLock.Acquire(_stateRoot);
        var store = CreateSettingsStore();
        if (await store.ReadAsync(cancellationToken) is not null)
            throw new InvalidOperationException("安装级设置已存在；初始化不会覆盖现有身份或数据库配置。");
        await EnsureNoRecoveryFencesAsync(cancellationToken);

        var normalizedDatabasePath = Path.GetFullPath(databasePath);
        var normalizedStorageRoot = Path.GetFullPath(storageRoot);
        var storageId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var profile = CreateSqliteProfile(normalizedDatabasePath, expectedDatabaseInstanceId: null, expectedStorageId: storageId);
        var provider = CreateSqliteProvider();
        await using var services = await provider.CreateNewAsync(
            profile, storageId, initialAdministratorPassword, cancellationToken);

        var settings = CreateSettings(
            benchId.Trim(),
            profile with { ExpectedDatabaseInstanceId = services.DatabaseInstanceId },
            CreateStorageProfile(normalizedStorageRoot, storageId));
        await store.SaveAsync(settings, expectedRevision: null, cancellationToken);
        return settings;
    }

    /// <summary>
    /// 已有升级库的显式登记流程。必须提供离线升级报告中的两个身份值；此流程不会写入数据库。
    /// </summary>
    public async Task<SystemSettings> AdoptExistingAsync(
        string benchId,
        string databasePath,
        string storageRoot,
        string expectedDatabaseInstanceId,
        string expectedStorageId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentityAndPaths(benchId, databasePath, storageRoot);
        if (!IsCanonicalGuid(expectedDatabaseInstanceId) || !IsCanonicalGuid(expectedStorageId))
            throw new ArgumentException("必须提供离线升级报告中的小写 GUID DatabaseInstanceId 和 StorageId。");

        using var processLock = InstallationProcessLock.Acquire(_stateRoot);
        var store = CreateSettingsStore();
        if (await store.ReadAsync(cancellationToken) is not null)
            throw new InvalidOperationException("安装级设置已存在；登记流程不会覆盖现有身份或数据库配置。");
        await EnsureNoRecoveryFencesAsync(cancellationToken);

        var storageRootFull = Path.GetFullPath(storageRoot);
        var profile = CreateSqliteProfile(Path.GetFullPath(databasePath), expectedDatabaseInstanceId, expectedStorageId);
        var provider = CreateSqliteProvider();
        var check = await provider.TestAsync(profile, cancellationToken);
        if (!check.Succeeded) throw new InvalidOperationException(check.Message);
        await using var services = await provider.OpenExistingAsync(profile, cancellationToken);

        var settings = CreateSettings(benchId.Trim(), profile, CreateStorageProfile(storageRootFull, expectedStorageId));
        await store.SaveAsync(settings, expectedRevision: null, cancellationToken);
        return settings;
    }

    private async Task<SystemSettings> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await CreateSettingsStore().ReadAsync(cancellationToken);
        return settings ?? throw new InvalidOperationException(
            $"未找到安装级设置：{Path.Combine(_stateRoot, "system-settings.json")}。请通过受控初始化或既有库登记入口配置；不会自动采用 app.json 路径或创建空库。");
    }

    private async Task<IReadOnlyList<XXX.TestBench.Core.Domain.Execution.OperationFence>> ReadFencesAsync(
        CancellationToken cancellationToken)
    {
        var store = new OperationFenceStore(_stateRoot, TimeProvider.System, FileGateTimeout);
        return await store.ReadAllAsync(cancellationToken);
    }

    private async Task EnsureNoRecoveryFencesAsync(CancellationToken cancellationToken)
    {
        var fences = await ReadFencesAsync(cancellationToken);
        if (fences.Count > 0)
            throw new InvalidOperationException("StateRoot 存在未闭合操作标记；不能初始化或登记数据库，须先由受控恢复流程处理。");
    }

    private void EnsureStateRootMatches(SystemSettings settings)
    {
        var configured = Path.GetFullPath(settings.StateRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(configured, _stateRoot, comparison))
            throw new InvalidDataException("system-settings.json 中的 StateRoot 与当前安装 StateRoot 不一致。");
    }

    private SystemSettingsStore CreateSettingsStore()
        => new(Path.Combine(_stateRoot, "system-settings.json"), TimeProvider.System, FileGateTimeout);

    private static SqliteDatabaseProvider CreateSqliteProvider()
        => new(new Pbkdf2PasswordHasher());

    private static DatabaseProfile CreateSqliteProfile(
        string databasePath,
        string? expectedDatabaseInstanceId,
        string expectedStorageId)
        => new()
        {
            ProfileId = Guid.NewGuid().ToString("D").ToLowerInvariant(),
            ProviderKey = "sqlite",
            DisplayName = "本地 SQLite",
            SqlitePath = databasePath,
            ConnectionTimeoutSeconds = 15,
            ExpectedDatabaseInstanceId = expectedDatabaseInstanceId,
            ExpectedStorageId = expectedStorageId
        };

    private SystemSettings CreateSettings(string benchId, DatabaseProfile profile, StorageProfile storage)
        => new()
        {
            SchemaVersion = SystemSettings.CurrentSchemaVersion,
            Revision = 0,
            BenchId = benchId,
            NodeId = Guid.NewGuid().ToString("D").ToLowerInvariant(),
            StateRoot = _stateRoot,
            DatabaseState = DatabaseActivationState.Active,
            ActiveProfile = profile,
            Storage = storage
        };

    private static StorageProfile CreateStorageProfile(string root, string storageId)
        => new(storageId, Path.Combine(root, "reports"), Path.Combine(root, "resources"), Path.Combine(root, "evidence"));

    private static void ValidateSetupInputs(
        string benchId,
        string databasePath,
        string storageRoot,
        string initialAdministratorPassword)
    {
        ValidateIdentityAndPaths(benchId, databasePath, storageRoot);
        if (string.IsNullOrWhiteSpace(initialAdministratorPassword) || initialAdministratorPassword.Length < 8)
            throw new ArgumentException("初始管理员密码至少需要 8 个字符。", nameof(initialAdministratorPassword));
    }

    private static void ValidateIdentityAndPaths(string benchId, string databasePath, string storageRoot)
    {
        if (string.IsNullOrWhiteSpace(benchId)) throw new ArgumentException("BenchId 必须由管理员明确提供。", nameof(benchId));
        if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath))
            throw new ArgumentException("数据库路径必须是绝对路径。", nameof(databasePath));
        if (string.IsNullOrWhiteSpace(storageRoot) || !Path.IsPathFullyQualified(storageRoot))
            throw new ArgumentException("存储根目录必须是绝对路径。", nameof(storageRoot));
    }

    private static bool IsCanonicalGuid(string value)
        => Guid.TryParseExact(value, "D", out var parsed)
           && string.Equals(value, parsed.ToString("D").ToLowerInvariant(), StringComparison.Ordinal);

    private static string ResolveDefaultStateRoot()
    {
        var commonDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonDataRoot))
            throw new InvalidOperationException("当前系统没有可用的 CommonApplicationData 路径，无法确定安装级 StateRoot。");
        return Path.Combine(commonDataRoot, "XXX.TestBench.Template", "state");
    }
}
