using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NavigationItemViewModel item } && DataContext is ShellViewModel shell)
            await shell.NavigateCommand.ExecuteAsync(item);
    }
}
