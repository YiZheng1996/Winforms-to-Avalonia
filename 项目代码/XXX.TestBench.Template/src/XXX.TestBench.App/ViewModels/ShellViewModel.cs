using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
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
        DeviceModeText = services is null ? "未知" : (services.DeviceConfig.DeviceMode == DeviceMode.Simulation ? "Simulation（仿真）" : "Hardware（硬件）");
        IsSimulationMode = services?.DeviceConfig.DeviceMode == DeviceMode.Simulation;
        DeviceModeDisplayText = services is null ? "模式未知" : (IsSimulationMode ? "仿真模式" : "硬件模式");
        ConnectionStatusText = isFaulted ? "故障" : services is null ? "未知" : (services.DeviceModes.Health == DeviceHealth.Healthy ? "运行正常" : services.DeviceModes.LastError ?? "运行正常");
        ConnectionDisplayText = isFaulted ? "PLC连接异常" : services is null ? "PLC状态未知" : (services.DeviceModes.Health == DeviceHealth.Healthy ? "PLC连接正常" : "PLC连接异常");
        BottomStatusText = $"数据库：{DatabasePath}  设备：{DeviceModeText}  版本：{Version}";
        IsFaulted = isFaulted;
        FaultMessage = faultMessage;
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
    public bool IsSimulationMode { get; }

    /// <summary>
    /// 设备模式的完整显示文字。
    /// </summary>
    public string DeviceModeText { get; }

    /// <summary>
    /// 设备模式的徽标显示文字。
    /// </summary>
    public string DeviceModeDisplayText { get; }

    /// <summary>
    /// 设备连接状态的短显示文字。
    /// </summary>
    public string ConnectionStatusText { get; }

    /// <summary>
    /// 设备连接状态的长显示文字。
    /// </summary>
    public string ConnectionDisplayText { get; }

    /// <summary>
    /// 底部状态栏的组合文字。
    /// </summary>
    public string BottomStatusText { get; }

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
                OnPropertyChanged(nameof(IsOverviewPage));
        }
    }

    /// <summary>
    /// 当前是否为运行总览页面。
    /// </summary>
    public bool IsOverviewPage => CurrentPage is OverviewViewModel;

    /// <summary>
    /// 当前登录用户，未登录时为空。
    /// </summary>
    public UserContext? CurrentUser => _currentUser;

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
                OnPropertyChanged(nameof(CurrentProductModelCodeText));
                OnPropertyChanged(nameof(CurrentProductTypeText));
                OnPropertyChanged(nameof(CurrentProductModelText));
                OnPropertyChanged(nameof(CurrentProductNumberText));
            }
        }
    }

    /// <summary>
    /// 当前产品型号代码的显示文字。
    /// </summary>
    public string CurrentProductModelCodeText => SelectedProductModel?.ProductModelCode ?? "未选择";

    /// <summary>
    /// 当前产品类型名称的显示文字。
    /// </summary>
    public string CurrentProductTypeText => SelectedProductModel?.ProductTypeName ?? "未选择";

    /// <summary>
    /// 当前产品型号的组合显示文字。
    /// </summary>
    public string CurrentProductModelText => SelectedProductModel is null
        ? "未选择"
        : $"{SelectedProductModel.ProductModelCode}  {SelectedProductModel.ProductModelName}";

    /// <summary>
    /// 当前产品编号的显示文字。
    /// </summary>
    public string CurrentProductNumberText => SelectedProductModel?.ProductModelCode ?? "未录入";

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
            .OrderBy(model => typeLookup[model.ProductTypeId].Name, StringComparer.CurrentCulture)
            .ThenBy(model => model.Code, StringComparer.OrdinalIgnoreCase)
            .Select(model =>
            {
                var type = typeLookup[model.ProductTypeId];
                return new ProductModelSelectionOption(
                    type.Id,
                    type.Code,
                    type.Name,
                    model.Id,
                    model.Code,
                    model.Name,
                    model.IsEnabled);
            })
            .ToArray();
    }

    /// <summary>
    /// 保存弹窗中选定的产品型号。
    /// </summary>
    public void SelectProductModel(ProductModelSelectionOption option)
        => SelectedProductModel = option;

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
    /// 退出命令：注销会话并清空登录状态与当前页面。
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
    }

    private void BuildNavigation()
    {
        if (_services is null || _currentUser is null) return;
        NavItems.Clear();
        var user = _currentUser;
        var pages = new (string Title, string DisplayTitle, string Icon, PermissionCode Permission, Func<PageViewModel> Create)[]
        {
            ( "运行总览", "工艺界面", "⌂", PermissionCode.ViewOverview, () => new OverviewViewModel(_services, user) ),
            ( "数据与报表", "报表界面", "▤", PermissionCode.ViewRecords, () => new DataReportsViewModel(_services, user) ),
            ( "参数管理", "参数管理", "☷", PermissionCode.ManageTestDefinitions, () => new RecipeCenterViewModel(_services, user) ),
            ( "任务管理", "数据查询", "⌕", PermissionCode.ManageTasks, () => new TaskManagementViewModel(_services, user) ),
            ( "试验执行", "试验执行", "▷", PermissionCode.ExecuteTests, () => new TestExecutionViewModel(_services, user) ),
            ( "工艺监控", "工艺监控", "⌁", PermissionCode.ManualControl, () => new ProcessMonitorViewModel(_services, user) ),
            ( "设备与校准", "硬件校准", "⚒", PermissionCode.ManageDevices, () => new DeviceCalibrationViewModel(_services, user) ),
            ( "日志诊断", "日志管理", "≡", PermissionCode.ViewLogs, () => new LogDiagnosticsViewModel(_services, user) ),
            ( "系统管理", "系统管理", "▣", PermissionCode.ManageUsers, () => new SystemManagementViewModel(_services, user) )
        };
        foreach (var (title, displayTitle, icon, permission, create) in pages)
        {
            if (user.HasPermission(permission))
                NavItems.Add(new NavigationItemViewModel(title, displayTitle, icon, create()));
        }
    }
}