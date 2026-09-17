using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class ProcessMonitorViewModel
{
    /// <summary>
    /// 是否显示明确开启的演示数据。该状态只影响页面展示，不改变真实缓存和写入链路。
    /// </summary>
    [ObservableProperty]
    private bool _isDemoData;

    public string DemoDataHintText
        => IsDemoData ? "演示数据显示中 · 不写入设备" : "设备数据未连接时显示未知";

    partial void OnIsDemoDataChanged(bool value)
    {
        foreach (var point in _processPoints.Values)
            point.SetDemoData(value);
        Diagram.SetDemoData(value);
        OnPropertyChanged(nameof(DemoDataHintText));
    }

    [RelayCommand]
    private void ToggleDemoData()
    {
        IsDemoData = !IsDemoData;
        StatusMessage = IsDemoData
            ? "已启用演示数据显示，不会写入设备"
            : "已关闭演示数据显示";
    }
}
