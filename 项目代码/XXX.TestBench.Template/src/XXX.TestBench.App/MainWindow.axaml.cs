using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.App.Views;

namespace XXX.TestBench.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer;

    public MainWindow()
    {
        InitializeComponent();
        UpdateClock();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        Closed += (_, _) => _clockTimer.Stop();
    }

    private async void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NavigationItemViewModel item } && DataContext is ShellViewModel shell)
            await shell.NavigateCommand.ExecuteAsync(item);
    }

    private async void OnNavigateToTitleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string title } || DataContext is not ShellViewModel shell)
            return;

        var item = shell.NavItems.FirstOrDefault(candidate => candidate.Title == title);
        if (item is not null)
            await shell.NavigateCommand.ExecuteAsync(item);
    }

    private async void OnProductSelectorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
            return;

        try
        {
            var options = await shell.LoadProductModelOptionsAsync();
            var dialog = new ProductModelSelectionWindow
            {
                DataContext = new ProductModelSelectionViewModel(options)
            };
            var selected = await ShowDialogWithOverlayAsync<ProductModelSelectionOption>(dialog);
            if (selected is not null)
                shell.SelectProductModel(selected);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"产品型号选择失败：{ex}");
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        CurrentDateTextBlock.Text = now.ToString("yyyy-MM-dd");
        CurrentTimeTextBlock.Text = now.ToString("HH:mm:ss");
    }

    public async Task<TResult?> ShowDialogWithOverlayAsync<TResult>(Window dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        DialogOverlay.IsVisible = true;
        try
        {
            return await dialog.ShowDialog<TResult>(this);
        }
        finally
        {
            DialogOverlay.IsVisible = false;
        }
    }
}
