using System.Windows.Input;

namespace XXX.TestBench.App.ViewModels;

/// <summary>异步命令：执行期间自动禁用并通知 CanExecuteChanged；异常统一交给 onError，避免 async void 崩溃。</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        RaiseCanExecuteChanged();
        try { await _execute(); }
        catch (Exception ex) { _onError?.Invoke(ex); }
        finally { _isRunning = false; RaiseCanExecuteChanged(); }
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        _isRunning = true;
        RaiseCanExecuteChanged();
        try { await _execute(); }
        catch (Exception ex) { _onError?.Invoke(ex); }
        finally { _isRunning = false; RaiseCanExecuteChanged(); }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>带参数异步命令。</summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    public RelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke((T?)parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        RaiseCanExecuteChanged();
        try { await _execute((T?)parameter); }
        catch (Exception ex) { _onError?.Invoke(ex); }
        finally { _isRunning = false; RaiseCanExecuteChanged(); }
    }

    public async Task ExecuteAsync(T? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        RaiseCanExecuteChanged();
        try { await _execute(parameter); }
        catch (Exception ex) { _onError?.Invoke(ex); }
        finally { _isRunning = false; RaiseCanExecuteChanged(); }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
