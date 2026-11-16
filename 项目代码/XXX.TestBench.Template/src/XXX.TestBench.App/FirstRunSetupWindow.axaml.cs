using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using XXX.TestBench.App.Composition;

namespace XXX.TestBench.App;

/// <summary>
/// 安装级设置缺失时提供明确的新库初始化或现有数据库选用入口。
/// </summary>
public partial class FirstRunSetupWindow : Window
{
    private bool _initialized;
    private bool _working;

    /// <summary>
    /// 提供 Avalonia 资源加载器和设计器使用的无参构造函数。
    /// </summary>
    public FirstRunSetupWindow()
        : this(startupReason: null)
    {
    }

    public FirstRunSetupWindow(string? startupReason)
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
        StartupReasonText.Text = startupReason ?? "尚未登记本机安装设置。请选择数据库配置方式并填写台架与存储信息。";
        _initialized = true;
        UpdateMode();
    }

    /// <summary>
    /// 安装级设置保存成功后通知应用重建组合根并进入登录。
    /// </summary>
    public event EventHandler? SetupCompleted;

    private async void OnSetupModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
            return;

        UpdateMode();
        if (SetupModeComboBox.SelectedIndex == 1)
            await InspectSelectedDatabaseAsync();
        else
            ClearDatabaseIdentity();
    }

    private void UpdateMode()
    {
        var adopting = SetupModeComboBox.SelectedIndex == 1;
        NewDatabasePanel.IsVisible = !adopting;
        AdoptDatabasePanel.IsVisible = adopting;
        DatabaseHelpText.Text = adopting
            ? "选择本模板的现有 SQLite 数据库，保留原有数据和用户账号。"
            : "新库路径必须尚不存在；选择已有文件会被拒绝，不会覆盖。";
        CompleteButton.Content = adopting ? "验证并使用现有数据库" : "创建新库并初始化管理员";
        StatusText.Text = string.Empty;
    }

    private async void OnBrowseDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { StorageProvider: { } storageProvider })
            return;

        if (SetupModeComboBox.SelectedIndex == 1)
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择现有 SQLite 数据库",
                AllowMultiple = false,
                FileTypeFilter = [CreateSqliteFileType()]
            });
            var selectedPath = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                DatabasePathTextBox.Text = selectedPath;
                await InspectSelectedDatabaseAsync();
            }
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择新 SQLite 数据库位置",
            SuggestedFileName = "testbench.db",
            DefaultExtension = "db",
            FileTypeChoices = [CreateSqliteFileType()]
        });
        var newDatabasePath = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(newDatabasePath))
            DatabasePathTextBox.Text = newDatabasePath;
        ClearDatabaseIdentity();
    }

    private async void OnBrowseStorageClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { StorageProvider: { } storageProvider })
            return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择文件存储根目录",
            AllowMultiple = false
        });
        StorageRootTextBox.Text = folders.FirstOrDefault()?.TryGetLocalPath() ?? StorageRootTextBox.Text;
    }

    private async Task InspectSelectedDatabaseAsync()
    {
        ClearDatabaseIdentity();
        var databasePath = DatabasePathTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(databasePath))
            return;

        SetStatus("正在只读检查数据库并读取身份编号…", isError: false);
        try
        {
            var identity = await new ApplicationBootstrapper()
                .ReadExistingDatabaseIdentityAsync(databasePath);
            if (SetupModeComboBox.SelectedIndex != 1
                || !string.Equals(DatabasePathTextBox.Text?.Trim(), databasePath, StringComparison.OrdinalIgnoreCase))
                return;

            DatabaseInstanceIdTextBox.Text = identity.DatabaseInstanceId;
            StorageIdTextBox.Text = string.IsNullOrEmpty(identity.StorageId)
                ? "使用此数据库时自动生成" : identity.StorageId;
            SetStatus(string.IsNullOrEmpty(identity.StorageId)
                ? "数据库检查通过；点击验证并使用后，将先备份数据库，再自动补齐文件存储身份。"
                : "数据库检查通过，身份信息已自动读取。", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void ClearDatabaseIdentity()
    {
        DatabaseInstanceIdTextBox.Text = string.Empty;
        StorageIdTextBox.Text = string.Empty;
    }

    private async void OnCompleteClick(object? sender, RoutedEventArgs e)
    {
        if (_working)
            return;

        var benchId = BenchIdTextBox.Text?.Trim() ?? string.Empty;
        var databasePath = DatabasePathTextBox.Text?.Trim() ?? string.Empty;
        var storageRoot = StorageRootTextBox.Text?.Trim() ?? string.Empty;
        var administratorPassword = AdministratorPasswordBox.Text ?? string.Empty;
        var confirmedPassword = ConfirmAdministratorPasswordBox.Text ?? string.Empty;
        var adopting = SetupModeComboBox.SelectedIndex == 1;

        if (!adopting && !string.Equals(administratorPassword, confirmedPassword, StringComparison.Ordinal))
        {
            SetStatus("两次输入的管理员密码不一致。", isError: true);
            return;
        }

        SetWorking(true);
        SetStatus(adopting ? "正在验证并使用现有数据库…" : "正在创建数据库和初始管理员…", isError: false);
        try
        {
            var bootstrapper = new ApplicationBootstrapper();
            if (adopting)
            {
                await bootstrapper.AdoptExistingAsync(
                    benchId,
                    databasePath,
                    storageRoot);
            }
            else
            {
                await bootstrapper.InitializeNewInstallationAsync(
                    benchId,
                    databasePath,
                    storageRoot,
                    administratorPassword);
            }

            SetupCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            SetWorking(false);
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void SetWorking(bool working)
    {
        _working = working;
        CompleteButton.IsEnabled = !working;
        SetupFieldsPanel.IsEnabled = !working;
        ExitButton.IsEnabled = !working;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(Color.Parse(isError ? "#D92D20" : "#475467"));
    }

    private static FilePickerFileType CreateSqliteFileType()
        => new("SQLite 数据库") { Patterns = ["*.db", "*.sqlite", "*.sqlite3"] };

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
