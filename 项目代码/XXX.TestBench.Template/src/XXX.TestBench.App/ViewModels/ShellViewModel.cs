using System.Collections.ObjectModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 应用外壳：登录/首次改密/退出、设备模式与连接状态、权限过滤导航、当前页切换、故障诊断。
/// 配置无效时以故障模式创建（services 为 null），仅显示诊断。
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly ShellServices? _services;
    private string _token = string.Empty;
    private UserContext? _currentUser;

    public ShellViewModel(ShellServices services) : this(services, false, null) { }

    public ShellViewModel(ShellServices? services, bool isFaulted, string? faultMessage)
    {
        _services = services;
        SystemName = services?.AppConfig.SystemName ?? "XXX 试验台通用上位机";
        Version = services?.Version ?? string.Empty;
        DatabasePath = services?.DatabasePath ?? string.Empty;
        DeviceModeText = services is null ? "未知" : (services.DeviceConfig.DeviceMode == DeviceMode.Simulation ? "Simulation（仿真）" : "Hardware（硬件）");
        IsSimulationMode = services?.DeviceConfig.DeviceMode == DeviceMode.Simulation;
        ConnectionStatusText = isFaulted ? "故障" : services is null ? "未知" : (services.DeviceModes.Health == DeviceHealth.Healthy ? "运行正常" : services.DeviceModes.LastError ?? "运行正常");
        BottomStatusText = $"数据库：{DatabasePath}  设备：{DeviceModeText}  版本：{Version}";
        IsFaulted = isFaulted;
        FaultMessage = faultMessage;

        LoginCommand = new RelayCommand(LoginAsync, () => !string.IsNullOrWhiteSpace(LoginName) && !string.IsNullOrWhiteSpace(Password));
        ChangePasswordCommand = new RelayCommand(ChangePasswordAsync, () => !string.IsNullOrWhiteSpace(NewPassword) && NewPassword == ConfirmPassword);
        LogoutCommand = new RelayCommand(LogoutAsync);
        NavigateCommand = new RelayCommand<NavigationItemViewModel>(NavigateAsync);
    }

    public string SystemName { get; }
    public string Version { get; }
    public string DatabasePath { get; }
    public bool IsSimulationMode { get; }
    public string DeviceModeText { get; }
    public string ConnectionStatusText { get; }
    public string BottomStatusText { get; }
    public bool IsFaulted { get; }
    public string? FaultMessage { get; }

    private bool _isAuthenticated;
    public bool IsAuthenticated { get => _isAuthenticated; private set => SetField(ref _isAuthenticated, value); }

    private bool _mustChangePassword;
    public bool MustChangePassword { get => _mustChangePassword; private set => SetField(ref _mustChangePassword, value); }

    private string _currentUserText = "未登录";
    public string CurrentUserText { get => _currentUserText; private set => SetField(ref _currentUserText, value); }

    private string _loginError = string.Empty;
    private bool _hasLoginError;
    public bool HasLoginError { get => _hasLoginError; private set => SetField(ref _hasLoginError, value); }
    public string LoginError { get => _loginError; private set { SetField(ref _loginError, value); HasLoginError = !string.IsNullOrEmpty(value); } }

    private string _loginName = string.Empty;
    public string LoginName { get => _loginName; set { if (SetField(ref _loginName, value)) LoginCommand.RaiseCanExecuteChanged(); } }

    private string _password = string.Empty;
    public string Password { get => _password; set { if (SetField(ref _password, value)) LoginCommand.RaiseCanExecuteChanged(); } }

    private string _newPassword = string.Empty;
    public string NewPassword { get => _newPassword; set { if (SetField(ref _newPassword, value)) ChangePasswordCommand.RaiseCanExecuteChanged(); } }

    private string _confirmPassword = string.Empty;
    public string ConfirmPassword { get => _confirmPassword; set { if (SetField(ref _confirmPassword, value)) ChangePasswordCommand.RaiseCanExecuteChanged(); } }

    private int _pendingUserId;

    public RelayCommand LoginCommand { get; }
    public RelayCommand ChangePasswordCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public RelayCommand<NavigationItemViewModel> NavigateCommand { get; }

    public ObservableCollection<NavigationItemViewModel> NavItems { get; } = new();

    private PageViewModel? _currentPage;
    public PageViewModel? CurrentPage { get => _currentPage; private set => SetField(ref _currentPage, value); }

    public UserContext? CurrentUser => _currentUser;

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

    public async Task LogoutAsync()
    {
        if (_services is not null && !string.IsNullOrEmpty(_token)) await _services.Authentication.RevokeAsync(_token);
        _token = string.Empty;
        _currentUser = null;
        IsAuthenticated = false;
        MustChangePassword = false;
        CurrentUserText = "未登录";
        NavItems.Clear();
        CurrentPage = null;
    }

    private void BuildNavigation()
    {
        if (_services is null || _currentUser is null) return;
        NavItems.Clear();
        var user = _currentUser;
        var pages = new (string Title, PermissionCode Permission, Func<PageViewModel> Create)[]
        {
            ( "运行总览", PermissionCode.ViewOverview, () => new OverviewViewModel(_services, user) ),
            ( "任务管理", PermissionCode.ManageTasks, () => new TaskManagementViewModel(_services, user) ),
            ( "试验执行", PermissionCode.ExecuteTests, () => new TestExecutionViewModel(_services, user) ),
            ( "配方中心", PermissionCode.ManageRecipes, () => new RecipeCenterViewModel(_services, user) ),
            ( "数据与报表", PermissionCode.ViewRecords, () => new DataReportsViewModel(_services, user) ),
            ( "工艺监控", PermissionCode.ManualControl, () => new ProcessMonitorViewModel(_services, user) ),
            ( "设备与校准", PermissionCode.ManageDevices, () => new DeviceCalibrationViewModel(_services, user) ),
            ( "系统管理", PermissionCode.ManageUsers, () => new SystemManagementViewModel(_services, user) ),
            ( "日志诊断", PermissionCode.ViewLogs, () => new LogDiagnosticsViewModel(_services, user) )
        };
        foreach (var (title, permission, create) in pages)
        {
            if (user.HasPermission(permission))
                NavItems.Add(new NavigationItemViewModel(title, create()));
        }
    }

    private async Task NavigateAsync(NavigationItemViewModel? item)
    {
        if (item is null) return;
        CurrentPage = item.Page;
        try { await item.Page.LoadAsync(); }
        catch (Exception ex) { item.Page.ReportError(ex); }
    }
}
