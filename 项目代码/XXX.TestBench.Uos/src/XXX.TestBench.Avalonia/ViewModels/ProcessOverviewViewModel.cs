using System.Collections.ObjectModel;

namespace XXX.TestBench.Avalonia.ViewModels;

/// <summary>
/// 汇总 P2 只读点和 Legacy 工艺信号组；输出元件只作为可追溯状态呈现，不提供写命令。
/// </summary>
public sealed class ProcessOverviewViewModel
{
    public ProcessOverviewViewModel(ObservableCollection<PointDisplayViewModel> availableSignals)
    {
        AvailableSignals = availableSignals ?? throw new ArgumentNullException(nameof(availableSignals));
        LegacySignalGroups = new ReadOnlyCollection<LegacySignalGroupViewModel>(
        [
            new("压力通道", "PE01～PE09", "Legacy 工艺界面可见；除当前 P2 五点外均保持通信未知"),
            new("电气量", "36V/160V 电压、电流", "Legacy 工艺界面可见；当前只读点表尚未闭合"),
            new("外部仪表", "WSD.CH00 / WSD.CH01", "当前仅 CH00 进入 P2 离线切片"),
            new("连接状态", "NoError / Simulated", "显示质量、时间戳和连接代次")
        ]);
        Outputs = new ReadOnlyCollection<OutputControlViewModel>(CreateOutputs().ToList());
    }

    public ObservableCollection<PointDisplayViewModel> AvailableSignals { get; }
    public IReadOnlyList<LegacySignalGroupViewModel> LegacySignalGroups { get; }
    public IReadOnlyList<OutputControlViewModel> Outputs { get; }
    public string BoundaryText =>
        "本页按 Legacy 工艺任务分组呈现只读状态。VX/DO/AO、故障复位和手动数值输出均不可操作，页面不会发送或缓存写命令。";

    private static IEnumerable<OutputControlViewModel> CreateOutputs()
    {
        for (var index = 1; index <= 12; index++)
            yield return new($"VX{index:00}", index is 7 or 10 ? "慢动作电磁阀" : "电磁阀", "写入禁用");

        yield return new("36V 供电", "电源控制", "写入禁用");
        yield return new("160V 供电", "电源控制", "写入禁用");
        yield return new("耐压合闸", "试验控制", "写入禁用");
        yield return new("电阻合闸", "试验控制", "写入禁用");
        yield return new("故障复位", "true→1000 ms→false 合同", "P3 安全审批前禁用");
    }
}

public sealed record LegacySignalGroupViewModel(string Title, string Points, string StatusText);

public sealed record OutputControlViewModel(string Name, string Purpose, string StatusText)
{
    public bool IsEnabled => false;
}
