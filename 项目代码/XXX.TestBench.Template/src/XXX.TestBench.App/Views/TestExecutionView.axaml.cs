using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 试验执行页面视图。
/// </summary>
public partial class TestExecutionView : UserControl
{
    /// <summary>
    /// 初始化试验执行页面。
    /// </summary>
    public TestExecutionView() => InitializeComponent();

    /// <summary>
    /// 点击执行按钮后运行对应的试验项。
    /// </summary>
    private async void OnExecuteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ExecutionItemRow row } && DataContext is TestExecutionViewModel vm)
            await vm.ExecuteItemAsync(row);
    }
}
