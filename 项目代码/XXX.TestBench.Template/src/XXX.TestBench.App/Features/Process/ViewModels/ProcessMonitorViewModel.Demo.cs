using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class ProcessMonitorViewModel
{
    /// <summary>
    /// 仿真运行时是否显示模拟数据。该状态只影响页面展示，不改变真实缓存和写入链路。
    /// </summary>
    [ObservableProperty]
    private bool _isDemoData;

    public string RuntimePresentationText
        => IsDemoData ? "仿真模式 · 模拟数据已加载" : RuntimeStatusText;

    public string RuntimePresentationHintText
        => IsDemoData
            ? "登录后自动使用当前仿真运行时的模拟数据，不会写入现场设备"
            : "页面显示当前设备运行时缓存，不会在界面刷新时另行读取设备";

    partial void OnIsDemoDataChanged(bool value)
    {
        foreach (var point in _processPoints.Values)
            point.SetDemoData(value);
        Diagram.SetDemoData(value);
        OnPropertyChanged(nameof(RuntimePresentationText));
        OnPropertyChanged(nameof(RuntimePresentationHintText));
    }

    private void ApplyRuntimePresentationMode(IDeviceRuntime? runtime)
    {
        IsDemoData = runtime?.IsSimulation == true;
    }
}
