using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.App.Icons;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 应用外壳视图模型：负责登录、首次改密、退出、设备模式与连接状态、
/// 按权限过滤导航、切换当前页面以及故障诊断。
/// 配置无效时以故障模式创建（服务为空），界面只显示诊断信息。
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ShellServices? _services;
    private string _token = string.Empty;
    private UserContext? _currentUser;
    private int _pendingUserId;

    public ShellViewModel(ShellServices services) : this(services, false, null) { }

    public ShellViewModel(ShellServices? services, bool isFaulted, string? faultMessage)
    {
        _services = services;
        SystemName = services?.AppConfig.SystemName ?? "XXX 试验台通用上位机";
        Version = services?.Version ?? string.Empty;
        DatabasePath = services?.DatabasePath ?? string.Empty;
        IsFaulted = isFaulted;
        FaultMessage = faultMessage;
        if (services is not null)
            services.DeviceModes.StateChanged += OnDeviceModeStateChanged;
    }

    /// <summary>
    /// 系统名称，显示在顶部标题栏。
    /// </summary>
    public string SystemName { get; }

    /// <summary>
    /// 程序版本号。
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 当前使用的数据库路径。
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    /// 是否处于仿真模式。
    /// </summary>
    public bool IsSimulationMode => _services?.DeviceModes.CurrentMode == DeviceMode.Simulation;

    /// <summary>
    /// 设备模式的完整显示文字。
    /// </summary>
    public string DeviceModeText => _services is null
        ? "未知"
        : _services.DeviceModes.CurrentMode switch
        {
            DeviceMode.Simulation => "仿真模式",
            DeviceMode.Hardware => "硬件模式",
            DeviceMode.Mixed => "按设备配置",
            _ => "模式未知"
        };

    /// <summary>
    /// 设备模式的徽标显示文字。
    /// </summary>
    public string DeviceModeDisplayText => _services is null
        ? "模式未知"
        : DeviceModeText;

    /// <summary>
    /// 设备连接状态的短显示文字。
    /// </summary>
    public string ConnectionStatusText => IsFaulted
        ? "故障"
        : _services is null
            ? "未知"
            : _services.DeviceModes.Health == DeviceHealth.Healthy
                ? "运行正常"
                : _services.DeviceModes.LastError ?? "运行异常";

    /// <summary>
    /// 设备连接状态的长显示文字。
    /// </summary>
    public string ConnectionDisplayText => IsFaulted
        ? "PLC连接异常"
        : _services is null
            ? "PLC状态未知"
            : _services.DeviceModes.Health == DeviceHealth.Healthy ? "PLC连接正常" : "PLC连接异常";

    /// <summary>
    /// 底部状态栏的组合文字。
    /// </summary>
    public string BottomStatusText => $"运行模式：{DeviceModeText}";

    private void OnDeviceModeStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsSimulationMode));
        OnPropertyChanged(nameof(DeviceModeText));
        OnPropertyChanged(nameof(DeviceModeDisplayText));
        OnPropertyChanged(nameof(ConnectionStatusText));
        OnPropertyChanged(nameof(ConnectionDisplayText));
        OnPropertyChanged(nameof(BottomStatusText));
    }

    /// <summary>
    /// 头部显示的当前日期文字，每次读取时取当前日期。
    /// </summary>
    public string HeaderDateText => DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>
    /// 头部显示的当前时间文字，每次读取时取当前时间。
    /// </summary>
    public string HeaderTimeText => DateTime.Now.ToString("HH:mm:ss");

    /// <summary>
    /// 系统是否处于启动故障状态。
    /// </summary>
    public bool IsFaulted { get; }

    /// <summary>
    /// 启动故障的详细信息。
    /// </summary>
    public string? FaultMessage { get; }

    private bool _isAuthenticated;

    /// <summary>
    /// 是否已完成登录，界面据此切换登录页与主界面。
    /// </summary>
    public bool IsAuthenticated { get => _isAuthenticated; private set => SetProperty(ref _isAuthenticated, value); }

    private bool _mustChangePassword;

    /// <summary>
    /// 是否强制要求首次改密。
    /// </summary>
    public bool MustChangePassword { get => _mustChangePassword; private set => SetProperty(ref _mustChangePassword, value); }

    private string _currentUserText = "未登录";

    /// <summary>
    /// 当前登录用户的显示文字，未登录时显示“未登录”。
    /// </summary>
    public string CurrentUserText { get => _currentUserText; private set => SetProperty(ref _currentUserText, value); }

    private string _loginError = string.Empty;
    private bool _hasLoginError;

    /// <summary>
    /// 是否已有登录错误。
    /// </summary>
    public bool HasLoginError { get => _hasLoginError; private set => SetProperty(ref _hasLoginError, value); }

    /// <summary>
    /// 登录错误文本；写入时同步刷新是否有错误。
    /// </summary>
    public string LoginError
    {
        get => _loginError;
        private set
        {
            if (SetProperty(ref _loginError, value))
                HasLoginError = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>
    /// 登录名；变化时自动刷新登录命令的可用状态。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _loginName = string.Empty;

    /// <summary>
    /// 登录密码；变化时自动刷新登录命令的可用状态。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    /// <summary>
    /// 新密码；变化时自动刷新改密命令的可用状态。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    private string _newPassword = string.Empty;

    /// <summary>
    /// 确认密码；变化时自动刷新改密命令的可用状态。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangePasswordCommand))]
    private string _confirmPassword = string.Empty;

    /// <summary>
    /// 左侧导航项集合。
    /// </summary>
    public ObservableCollection<NavigationItemViewModel> NavItems { get; } = new();

    private PageViewModel? _currentPage;

    /// <summary>
    /// 当前显示的页面；变化时同时通知“是否为运行总览页”。
    /// </summary>
    public PageViewModel? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsOverviewPage));
                OnPropertyChanged(nameof(IsProcessMonitorPage));
                OnPropertyChanged(nameof(ShowProductInfoBar));
                OnPropertyChanged(nameof(ShowGlobalStatusBar));
            }
        }
    }

    /// <summary>
    /// 当前是否为运行总览页面。
    /// </summary>
    public bool IsOverviewPage => CurrentPage is OverviewViewModel;

    /// <summary>
    /// 当前是否为工艺监控页面；页面级产品上下文不改变总览页判定。
    /// </summary>
    public bool IsProcessMonitorPage => CurrentPage is ProcessMonitorViewModel;

    /// <summary>
    /// 产品上下文栏只在保留的总览页面显示；工艺界面使用自己的整页布局。
    /// </summary>
    public bool ShowProductInfoBar => IsOverviewPage;

    /// <summary>
    /// 工艺界面已包含统一状态区，避免与外壳底部状态条重复显示。
    /// </summary>
    public bool ShowGlobalStatusBar => !IsProcessMonitorPage;

    /// <summary>
    /// 当前登录用户，未登录时为空。
    /// </summary>
    public UserContext? CurrentUser => _currentUser;

    /// <summary>
    /// 认证流程完成事件。桌面层据此把登录窗口替换为工艺主窗口。
    /// </summary>
    public event EventHandler? AuthenticationSucceeded;

    private ProductModelSelectionOption? _selectedProductModel;

    /// <summary>
    /// 当前选择的产品型号；变化时刷新四个派生的显示文本。
    /// </summary>
    public ProductModelSelectionOption? SelectedProductModel
    {
        get => _selectedProductModel;
        private set
        {
            if (SetProperty(ref _selectedProductModel, value))
            {
                OnPropertyChanged(nameof(CurrentProductModelNameText));
                OnPropertyChanged(nameof(CurrentProductTypeText));
                OnPropertyChanged(nameof(CurrentProductModelText));
                OnPropertyChanged(nameof(CurrentProductNumberText));
            }
        }
    }

    /// <summary>
    /// 当前产品型号名称的显示文字。
    /// </summary>
    public string CurrentProductModelNameText => SelectedProductModel?.ProductModelName ?? "未选择";

    /// <summary>
    /// 当前产品类型名称的显示文字。
    /// </summary>
    public string CurrentProductTypeText => SelectedProductModel?.ProductTypeName ?? "未选择";

    /// <summary>
    /// 当前产品型号的显示文字。
    /// </summary>
    public string CurrentProductModelText => SelectedProductModel is null
        ? "未选择"
        : SelectedProductModel.ProductModelName;

    /// <summary>
    /// 当前产品编号的显示文字。
    /// </summary>
    public string CurrentProductNumberText => "未录入";

    /// <summary>
    /// 读取可选产品型号列表，供型号选择弹窗使用。
    /// </summary>
    public async Task<IReadOnlyList<ProductModelSelectionOption>> LoadProductModelOptionsAsync(CancellationToken ct = default)
    {
        if (_services is null) return Array.Empty<ProductModelSelectionOption>();

        var types = await _services.ProductRepository.ListTypesAsync(includeDisabled: false, ct);
        var typeLookup = types.ToDictionary(type => type.Id);
        var models = await _services.ProductRepository.ListModelsAsync(null, includeDisabled: false, ct);
        return models
            .Where(model => typeLookup.ContainsKey(model.ProductTypeId))
            .OrderByDescending(model => model.CreatedAtUtc)
            .ThenByDescending(model => model.Id)
            .Select(model =>
            {
                var type = typeLookup[model.ProductTypeId];
                return new ProductModelSelectionOption(
                    type.Id,
                    type.Name,
                    model.Id,
                    model.Name,
                    model.IsEnabled,
                    model.CreatedAtUtc);
            })
            .ToArray();
    }

    /// <summary>
    /// 保存弹窗中选定的产品型号。
    /// </summary>
    public void SelectProductModel(ProductModelSelectionOption option)
        => SelectedProductModel = option;

    /// <summary>
    /// 产品选择弹窗打开前的活动试验保护。
    /// </summary>
    public async Task<OperationFeedback> CheckProductModelChangeAllowedAsync(
        CancellationToken ct = default)
    {
        if (_services is null)
            return OperationFeedback.Failure("系统处于故障状态，不能更换产品型号");
        if (await _services.RecordRepository.GetActiveRunningRecordAsync(ct) is not null)
            return OperationFeedback.Failure("活动试验期间不能更换产品型号");
        return OperationFeedback.Success(string.Empty);
    }

    /// <summary>
    /// 产品选择应用前再次执行活动试验保护，选择仅更新产品上下文，不改变工艺绑定和量程。
    /// </summary>
    public async Task<OperationFeedback> TrySelectProductModelAsync(
        ProductModelSelectionOption option,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        var allowed = await CheckProductModelChangeAllowedAsync(ct);
        if (!allowed.Succeeded)
            return allowed;
        SelectedProductModel = option;
        return OperationFeedback.Success("产品型号已更新");
    }

    /// <summary>
    /// 登录命令：账号与密码均非空时才可执行。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLogin))]
    public async Task LoginAsync()
    {
        if (_services is null) { LoginError = "系统处于故障状态，无法登录"; return; }
        LoginError = string.Empty;
        var result = await _services.Authentication.LoginAsync(LoginName.Trim(), Password);
        if (!result.Success)
        {
            LoginError = result.Error ?? "登录失败";
            HasLoginError = true;
            return;
        }
        _pendingUserId = result.User!.Id;
        _token = result.Session!.Token;
        if (result.User.MustChangePassword)
        {
            MustChangePassword = true;
            LoginError = "首次登录须修改密码后再进入系统";
            HasLoginError = true;
            return;
        }
        await EnterAuthenticatedAsync();
    }

    /// <summary>
    /// 判断登录命令当前是否可用：账号与密码都非空。
    /// </summary>
    private bool CanLogin() => !string.IsNullOrWhiteSpace(LoginName) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>
    /// 改密命令：新密码与确认密码一致且非空时才可执行。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangePassword))]
    public async Task ChangePasswordAsync()
    {
        if (_services is null) return;
        LoginError = string.Empty;
        try
        {
            await _services.Authentication.ChangePasswordAsync(_pendingUserId, Password, NewPassword);
            await EnterAuthenticatedAsync();
        }
        catch (Exception ex)
        {
            LoginError = ex.Message;
            HasLoginError = true;
        }
    }

    /// <summary>
    /// 判断改密命令当前是否可用：新密码非空且两次输入一致。
    /// </summary>
    private bool CanChangePassword() => !string.IsNullOrWhiteSpace(NewPassword) && NewPassword == ConfirmPassword;

    /// <summary>
    /// 注销当前会话并清空登录状态与当前页面；主窗口关闭由桌面窗口层负责。
    /// </summary>
    [RelayCommand]
    public async Task LogoutAsync()
    {
        if (_services is not null && !string.IsNullOrEmpty(_token)) await _services.Authentication.RevokeAsync(_token);
        _token = string.Empty;
        _currentUser = null;
        IsAuthenticated = false;
        MustChangePassword = false;
        CurrentUserText = "未登录";
        SelectedProductModel = null;
        NavItems.Clear();
        CurrentPage = null;
    }

    /// <summary>
    /// 页面切换命令：选中对应导航项并加载其页面。
    /// </summary>
    [RelayCommand]
    public async Task NavigateAsync(NavigationItemViewModel? item)
    {
        if (item is null) return;
        foreach (var navItem in NavItems)
            navItem.IsSelected = ReferenceEquals(navItem, item);
        CurrentPage = item.Page;
        try { await item.Page.LoadAsync(); }
        catch (Exception ex) { item.Page.ReportError(ex); }
    }

    private async Task EnterAuthenticatedAsync()
    {
        if (_services is null) return;
        _currentUser = await _services.Authentication.BuildUserContextAsync(_token);
        IsAuthenticated = true;
        MustChangePassword = false;
        CurrentUserText = _currentUser.DisplayName;
        Password = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        BuildNavigation();
        if (NavItems.Count > 0) await NavigateAsync(NavItems[0]);
        AuthenticationSucceeded?.Invoke(this, EventArgs.Empty);
    }

    private void BuildNavigation()
    {
        if (_services is null || _currentUser is null) return;
        NavItems.Clear();
        var user = _currentUser;
        var pages = new (string Title, string DisplayTitle, AppIconKind Icon, Func<UserContext, bool> CanOpen, Func<PageViewModel> Create)[]
        {
            ( "运行总览", "工艺界面", AppIconKind.Overview, actor => actor.HasPermission(PermissionCode.ViewOverview), () => new ProcessMonitorViewModel(_services, user) ),
            ( "数据与报表", "报表界面", AppIconKind.Reports, actor => actor.HasPermission(PermissionCode.ViewRecords), () => new DataReportsViewModel(_services, user) ),
            ( "参数管理", "参数管理", AppIconKind.Parameters, actor => actor.HasPermission(PermissionCode.ManageProducts)
                || actor.HasPermission(PermissionCode.ManageTestPoints)
                || actor.HasPermission(PermissionCode.ManageTestDefinitions), () => new ParameterManagementViewModel(_services, user) ),
            ( "试验执行", "试验执行", AppIconKind.TestExecution, actor => actor.HasPermission(PermissionCode.ExecuteTests), () => new TestExecutionViewModel(_services, user) ),
            ( "设备与校准", "硬件校准", AppIconKind.Calibration, actor => actor.HasPermission(PermissionCode.ManageDevices), () => new DeviceCalibrationViewModel(_services, user) ),
            ( "设备点位", "设备点位", AppIconKind.DevicePoints, actor => actor.HasPermission(PermissionCode.ManageDevices), () => new DevicePointManagementViewModel(_services, user) ),
            ( "日志诊断", "日志管理", AppIconKind.Logs, actor => actor.HasPermission(PermissionCode.ViewLogs), () => new LogDiagnosticsViewModel(_services, user) ),
            ( "系统管理", "系统管理", AppIconKind.SystemManagement, actor => actor.HasPermission(PermissionCode.ManageUsers)
                || actor.HasPermission(PermissionCode.ManageRoles), () => new SystemManagementViewModel(_services, user) )
        };
        foreach (var (title, displayTitle, icon, canOpen, create) in pages)
        {
            if (canOpen(user))
                NavItems.Add(new NavigationItemViewModel(title, displayTitle, icon, create()));
        }
    }
}
