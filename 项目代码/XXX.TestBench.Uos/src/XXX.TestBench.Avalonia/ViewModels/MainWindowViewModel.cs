using System.Collections.ObjectModel;
using System.Windows.Input;
using XXX.TestBench.Avalonia.Composition;
using XXX.TestBench.Avalonia.Localization;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Gateway.Domain;
using XXX.TestBench.Gateway.Infrastructure;

namespace XXX.TestBench.Avalonia.ViewModels;

/// <summary>
/// Avalonia 组合后的主状态源：负责导航和 Core/只读 Gateway 协调，不直接访问 PLC、数据库或仪器。
/// </summary>
public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly TestBenchStateMachine _stateMachine;
    private readonly ReadOnlyGatewayRuntime? _runtime;
    private readonly UiGatewayLogSink? _log;
    private readonly string _configPath;
    private readonly string _transportMode;
    private readonly string? _startupError;
    private readonly PresentationTextCatalog _texts;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private BenchStateSnapshot _state;
    private GatewayPollSnapshot? _gatewaySnapshot;
    private string _productId = "B11-Offline-Demo";
    private string _feedbackText;
    private string _lastLogText = "暂无日志";
    private bool _isBusy;
    private bool _started;
    private bool _disposed;
    private NavigationItemViewModel _selectedNavigationItem;

    public MainWindowViewModel(
        TestBenchStateMachine stateMachine,
        ReadOnlyGatewayRuntime? runtime,
        UiGatewayLogSink? log,
        string configPath,
        string transportMode,
        string? startupError = null,
        PresentationTextCatalog? texts = null)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _runtime = runtime;
        _log = log;
        _configPath = configPath;
        _transportMode = transportMode;
        _startupError = startupError;
        _texts = texts ?? new PresentationTextCatalog();
        _state = _stateMachine.Snapshot;
        _feedbackText = startupError ?? "等待初始化通信和应用核心。";

        Points = new ObservableCollection<PointDisplayViewModel>(
            P2ReadOnlyPointCatalog.Points.Select(x => new PointDisplayViewModel(x)));

        RefreshCommand = new AsyncCommand(RefreshAsync, () => CanRefresh, HandleCommandError);
        LoginCommand = new AsyncCommand(LoginAsync, () => CanLogin, HandleCommandError);
        SelectProductCommand = new AsyncCommand(SelectProductAsync, () => CanSelectProduct, HandleCommandError);
        StartAutomaticTestCommand = new AsyncCommand(StartAutomaticTestAsync, () => CanStartAutomaticTest, HandleCommandError);
        EnterManualCommand = new AsyncCommand(EnterManualAsync, () => CanEnterManual, HandleCommandError);
        StopCommand = new AsyncCommand(StopAsync, () => CanStop, HandleCommandError);
        RecoverFaultCommand = new AsyncCommand(RecoverFaultAsync, () => CanRecoverFault, HandleCommandError);

        if (_log is not null)
            _log.EntryWritten += OnLogEntry;

        // 按用户任务而不是 Legacy 控件类型拆分页面；每个页面共享同一 Core/Gateway 状态源。
        TestOperation = new TestOperationViewModel(this);
        ProcessOverview = new ProcessOverviewViewModel(Points);
        Management = new ManagementViewModel();
        Reports = new ReportsViewModel();
        Calibration = new CalibrationViewModel();
        Diagnostics = new DiagnosticsViewModel(_log);
        Instrument = new InstrumentViewModel();

        NavigationItems = new ReadOnlyCollection<NavigationItemViewModel>(
        [
            CreateNavigation("overview", "Nav.Overview"),
            CreateNavigation("test", "Nav.Test"),
            CreateNavigation("process", "Nav.Process"),
            CreateNavigation("management", "Nav.Management"),
            CreateNavigation("reports", "Nav.Reports"),
            CreateNavigation("calibration", "Nav.Calibration"),
            CreateNavigation("diagnostics", "Nav.Diagnostics"),
            CreateNavigation("instrument", "Nav.Instrument")
        ]);
        _selectedNavigationItem = NavigationItems[0];
        NavigateCommand = new RelayCommand(Navigate);
    }

    public ObservableCollection<PointDisplayViewModel> Points { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand SelectProductCommand { get; }
    public ICommand StartAutomaticTestCommand { get; }
    public ICommand EnterManualCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RecoverFaultCommand { get; }
    public ICommand NavigateCommand { get; }
    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }
    public TestOperationViewModel TestOperation { get; }
    public ProcessOverviewViewModel ProcessOverview { get; }
    public ManagementViewModel Management { get; }
    public ReportsViewModel Reports { get; }
    public CalibrationViewModel Calibration { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public InstrumentViewModel Instrument { get; }

    public string AppTitle => _texts.Get("App.Title");
    public NavigationItemViewModel SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (value is null || !SetProperty(ref _selectedNavigationItem, value))
                return;
            OnPropertyChanged(nameof(SelectedPageTitle));
            OnPropertyChanged(nameof(SelectedPageDescription));
            OnPropertyChanged(nameof(IsOverviewVisible));
            OnPropertyChanged(nameof(IsTestOperationVisible));
            OnPropertyChanged(nameof(IsProcessOverviewVisible));
            OnPropertyChanged(nameof(IsManagementVisible));
            OnPropertyChanged(nameof(IsReportsVisible));
            OnPropertyChanged(nameof(IsCalibrationVisible));
            OnPropertyChanged(nameof(IsDiagnosticsVisible));
            OnPropertyChanged(nameof(IsInstrumentVisible));
        }
    }

    public string SelectedPageTitle => SelectedNavigationItem.Title;
    public string SelectedPageDescription => SelectedNavigationItem.Description;
    public bool IsOverviewVisible => IsPage("overview");
    public bool IsTestOperationVisible => IsPage("test");
    public bool IsProcessOverviewVisible => IsPage("process");
    public bool IsManagementVisible => IsPage("management");
    public bool IsReportsVisible => IsPage("reports");
    public bool IsCalibrationVisible => IsPage("calibration");
    public bool IsDiagnosticsVisible => IsPage("diagnostics");
    public bool IsInstrumentVisible => IsPage("instrument");
    public string ProductId
    {
        get => _productId;
        set => SetProperty(ref _productId, value);
    }

    public string StatusText => _state.State switch
    {
        BenchState.Starting => "启动中",
        BenchState.Unauthenticated => "待登录",
        BenchState.Ready => "就绪",
        BenchState.Manual => "手动模式",
        BenchState.AutomaticRunning => "自动试验中",
        BenchState.Stopping => "安全停止中",
        BenchState.Completed => "已完成",
        BenchState.Faulted => "故障",
        _ => _state.State.ToString()
    };

    public string ConnectionText
    {
        get
        {
            if (_gatewaySnapshot is null)
                return "等待首样本";
            var source = _gatewaySnapshot.IsSimulated ? "仿真" : "设备";
            var quality = _gatewaySnapshot.IsHealthy ? "Good" : "Bad";
            var generation = _gatewaySnapshot.Points.Max(x => x.ConnectionGeneration);
            return $"{source} · {quality} · 第 {generation} 代";
        }
    }

    public string SafetyText
    {
        get
        {
            var point = _gatewaySnapshot?.Find("SMART.PLC.DI.MDI00");
            return point?.Quality switch
            {
                DataQuality.Good when point.Value is bool value => value
                    ? "DI00 安全联锁：true；但 Test00 尚未接入，自动/手动控制仍保持禁用。"
                    : "DI00 安全联锁：false；自动试验禁止启动。",
                DataQuality.Bad => "DI00 安全联锁：Bad；不得把通信失败当作 false 或安全通过。",
                _ => "DI00 安全联锁：通信未知；不得启动控制流程。"
            };
        }
    }

    public string WorkflowText =>
        "初始化 → 登录 → 选择产品 → 校验 DI00/Test00 → 才能进入控制流程。当前首条切片为压力调整阀 B11，仍处于 P2 只读阶段。";

    public string TransportModeText => $"TransportMode：{_transportMode}";
    public string TestModeText => _state.Gateway.Test00.Quality switch
    {
        CoreQuality.Good when _state.Gateway.Test00.Value == TestMode.Automatic => "Test00：自动",
        CoreQuality.Good when _state.Gateway.Test00.Value == TestMode.Manual => "Test00：手动",
        CoreQuality.Bad => "Test00：Bad",
        _ => "Test00：通信未知（尚未纳入 P2 五点）"
    };

    public string WritesText => "写入能力：禁用。Zero/Gain 不会因启动、刷新或打开页面而写入。";
    public string ConfigText => string.IsNullOrWhiteSpace(_configPath) ? "配置：不可用" : $"配置：{_configPath}";
    public string FeedbackText => _feedbackText;
    public string LastLogText => _lastLogText;
    public string LastSampleText => _gatewaySnapshot is null
        ? "尚未采样"
        : $"采样：{_gatewaySnapshot.Timestamp.LocalDateTime:HH:mm:ss.fff}";

    public bool IsBusy => _isBusy;
    public bool CanRefresh => !_isBusy && _runtime is not null;
    public bool CanLogin => !_isBusy && _runtime?.IsSimulated == true && _state.State == BenchState.Unauthenticated;
    public bool CanSelectProduct => !_isBusy && _state.State is BenchState.Ready or BenchState.Manual;
    public bool CanStartAutomaticTest => !_isBusy
        && _state.State == BenchState.Ready
        && !string.IsNullOrWhiteSpace(_state.ProductId)
        && _state.Gateway.IsReadyForControl
        && _state.Gateway.DI00.Value == true
        && _state.Gateway.Test00.Value == TestMode.Automatic;
    public bool CanEnterManual => !_isBusy
        && _state.State == BenchState.Ready
        && _state.Gateway.IsReadyForControl
        && _state.Gateway.Test00.Value == TestMode.Manual;
    public bool CanStop => !_isBusy && _state.State == BenchState.AutomaticRunning;
    public bool CanRecoverFault => !_isBusy && _state.State == BenchState.Faulted;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            return;
        _started = true;

        if (_runtime is null)
        {
            ApplyCoreResult(_stateMachine.Initialize(configurationAvailable: false, databaseAvailable: false));
            _feedbackText = _startupError ?? "配置或 Gateway 初始化失败。";
            NotifyState();
            return;
        }

        await RefreshCoreAsync(cancellationToken).ConfigureAwait(true);
        // 刷新层会把取消转换成可见状态；启动层必须再次检查令牌，避免窗口退出时
        // 仍把 Core 从 Starting 推进到可操作状态。
        if (cancellationToken.IsCancellationRequested)
        {
            _started = false;
            return;
        }
        if (_state.State == BenchState.Starting)
            ApplyCoreResult(_stateMachine.Initialize(configurationAvailable: true, databaseAvailable: true));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        if (_log is not null)
            _log.EntryWritten -= OnLogEntry;
        TestOperation.Dispose();
        Diagnostics.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private Task RefreshAsync() => RefreshCoreAsync(_lifetimeCancellation.Token);

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (_runtime is null)
        {
            _feedbackText = "Gateway Runtime 不可用，当前仅显示配置错误。";
            NotifyState();
            return;
        }

        SetBusy(true);
        try
        {
            var snapshot = await _runtime.PollOnceAsync(cancellationToken).ConfigureAwait(true);
            ApplyGatewaySnapshot(snapshot);
            if (_state.State is not (BenchState.Stopping or BenchState.Faulted))
                _feedbackText = snapshot.IsHealthy ? "只读数据已刷新。" : "只读数据已刷新，但存在 Bad 点。";
            OnPropertyChanged(nameof(FeedbackText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _feedbackText = "只读刷新已取消。";
            OnPropertyChanged(nameof(FeedbackText));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Task LoginAsync()
    {
        if (_runtime?.IsSimulated != true)
        {
            _feedbackText = "真实认证服务尚未接入，ConfiguredDevices 模式不会伪造登录成功。";
            OnPropertyChanged(nameof(FeedbackText));
            return Task.CompletedTask;
        }

        ApplyCoreResult(_stateMachine.Login(authenticated: true));
        _feedbackText = "已进入离线仿真会话；这不是现场权限认证。";
        OnPropertyChanged(nameof(FeedbackText));
        return Task.CompletedTask;
    }

    private Task SelectProductAsync()
    {
        ApplyCoreResult(_stateMachine.SelectProduct(ProductId));
        return Task.CompletedTask;
    }

    private Task StartAutomaticTestAsync()
    {
        ApplyCoreResult(_stateMachine.StartAutomaticTest());
        return Task.CompletedTask;
    }

    private Task EnterManualAsync()
    {
        ApplyCoreResult(_stateMachine.EnterManualMode());
        return Task.CompletedTask;
    }

    private Task StopAsync()
    {
        // 本阶段只请求 Core 的停止状态迁移；物理安全收尾属于后续经审批的写入适配器，
        // 这里不能用一个“成功”提示伪造设备已经复位或断能。
        ApplyCoreResult(_stateMachine.RequestStop(StopReason.OperatorRequested));
        return Task.CompletedTask;
    }

    private Task RecoverFaultAsync()
    {
        ApplyCoreResult(_stateMachine.RecoverFault());
        return Task.CompletedTask;
    }

    private void ApplyGatewaySnapshot(GatewayPollSnapshot snapshot)
    {
        _gatewaySnapshot = snapshot;
        foreach (var point in snapshot.Points)
            Points.FirstOrDefault(x => x.PointId == point.PointId)?.Apply(point);

        var di = snapshot.Find("SMART.PLC.DI.MDI00");
        var diValue = di?.Value is bool boolean ? (bool?)boolean : null;
        var generation = snapshot.Points.Max(x => x.ConnectionGeneration);
        var gateway = new GatewayReadinessSnapshot(
            SessionValid: snapshot.IsHealthy,
            CriticalPointsGood: snapshot.IsHealthy,
            DI00: new CoreSignal<bool?>(diValue, ToCoreQuality(di?.Quality ?? DataQuality.Unknown), di?.Timestamp ?? snapshot.Timestamp, di?.ConnectionGeneration ?? generation),
            Test00: new CoreSignal<TestMode?>(null, CoreQuality.Unknown, snapshot.Timestamp, generation),
            ConnectionGeneration: generation,
            IsSimulated: snapshot.IsSimulated);
        ApplyCoreResult(_stateMachine.UpdateGateway(gateway));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(SafetyText));
        OnPropertyChanged(nameof(LastSampleText));
    }

    private void ApplyCoreResult(CoreOperationResult result)
    {
        _state = result.Snapshot;
        if (result.Diagnostic is not null)
            _feedbackText = result.Diagnostic.Message;
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(TestModeText));
        OnPropertyChanged(nameof(FeedbackText));
        RaiseCommands();
    }

    private void SetBusy(bool value)
    {
        if (!SetProperty(ref _isBusy, value))
            return;
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        ((AsyncCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)LoginCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SelectProductCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)StartAutomaticTestCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)EnterManualCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)StopCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)RecoverFaultCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanLogin));
        OnPropertyChanged(nameof(CanSelectProduct));
        OnPropertyChanged(nameof(CanStartAutomaticTest));
        OnPropertyChanged(nameof(CanEnterManual));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanRecoverFault));
    }

    private void OnLogEntry(GatewayLogEntry entry)
    {
        _lastLogText = $"{entry.Level}: {entry.Message}";
        OnPropertyChanged(nameof(LastLogText));
    }

    private void HandleCommandError(Exception exception)
    {
        _feedbackText = $"命令执行失败：{exception.Message}";
        OnPropertyChanged(nameof(FeedbackText));
    }

    private static CoreQuality ToCoreQuality(DataQuality quality) => quality switch
    {
        DataQuality.Good => CoreQuality.Good,
        DataQuality.Uncertain => CoreQuality.Uncertain,
        DataQuality.Bad => CoreQuality.Bad,
        _ => CoreQuality.Unknown
    };

    private NavigationItemViewModel CreateNavigation(string key, string resourcePrefix) => new(
        key,
        _texts.Get(resourcePrefix),
        _texts.Get($"{resourcePrefix}.Description"));

    private bool IsPage(string key) => string.Equals(SelectedNavigationItem.Key, key, StringComparison.Ordinal);

    private void Navigate(object? parameter)
    {
        if (parameter is not string key)
            return;
        var item = NavigationItems.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
        if (item is not null)
            SelectedNavigationItem = item;
    }
}
