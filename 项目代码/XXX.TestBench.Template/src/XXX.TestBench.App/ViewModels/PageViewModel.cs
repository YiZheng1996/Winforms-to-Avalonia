namespace XXX.TestBench.App.ViewModels;

/// <summary>页面 ViewModel 基类：标题、加载、忙碌与状态消息。</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; protected set => SetField(ref _isBusy, value); }

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; protected set => SetField(ref _statusMessage, value); }

    public virtual Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

    public void ReportError(Exception ex) => StatusMessage = ex.Message;
}
