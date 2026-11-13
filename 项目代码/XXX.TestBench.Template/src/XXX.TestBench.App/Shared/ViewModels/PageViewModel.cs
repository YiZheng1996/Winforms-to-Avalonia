using CommunityToolkit.Mvvm.ComponentModel;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 页面视图模型基类：提供标题、加载入口、忙碌状态与状态消息。
/// 使用工具包提供的通知基类，所有页面视图模型都从这里继承。
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    /// <summary>
    /// 页面在左侧导航中显示的名称，由每个页面自行给出。
    /// </summary>
    public abstract string Title { get; }

    private bool _isBusy;

    /// <summary>
    /// 页面是否正在后台加载数据，界面据此显示等待效果。
    /// 只允许子类修改，外部只能读取。
    /// </summary>
    public bool IsBusy { get => _isBusy; protected set => SetProperty(ref _isBusy, value); }

    private string _statusMessage = string.Empty;

    /// <summary>
    /// 页面底部展示的操作结果或错误提示文字。
    /// 只允许子类修改，外部只能读取。
    /// </summary>
    public string StatusMessage { get => _statusMessage; protected set => SetProperty(ref _statusMessage, value); }

    /// <summary>
    /// 页面首次显示或手动刷新时调用的加载入口，默认不做任何事。
    /// </summary>
    public virtual Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// 把异常信息写入状态消息，供界面统一展示错误。
    /// </summary>
    public void ReportError(Exception ex) => StatusMessage = ex.Message;
}