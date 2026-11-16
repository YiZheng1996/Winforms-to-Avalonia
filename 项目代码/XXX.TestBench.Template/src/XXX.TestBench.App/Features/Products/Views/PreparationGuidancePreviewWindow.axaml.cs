using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>已保存准备指导的只读预览窗口。</summary>
public partial class PreparationGuidancePreviewWindow : Window
{
    public PreparationGuidancePreviewWindow() => InitializeComponent();

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is PreparationGuidancePreviewViewModel viewModel)
            viewModel.Dispose();
        base.OnClosed(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
