using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

public partial class TestExecutionView : UserControl
{
    public TestExecutionView() => InitializeComponent();

    private async void OnExecuteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ExecutionItemRow row } && DataContext is TestExecutionViewModel vm)
            await vm.ExecuteItemAsync(row);
    }
}
