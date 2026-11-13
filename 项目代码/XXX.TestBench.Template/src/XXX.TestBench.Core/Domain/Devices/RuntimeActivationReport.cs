namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 运行时启动时某台设备未能激活的原因。
/// </summary>
public sealed record DeviceActivationIssue(
    string DeviceId,
    string DeviceName,
    string Message,
    Exception? Exception = null);

/// <summary>
/// 候选配置或当前运行时的设备级激活结果。即使部分设备失败也保留完整报告。
/// </summary>
public sealed record RuntimeActivationReport(
    int TotalDevices,
    int ActivatedDevices,
    IReadOnlyList<DeviceActivationIssue> Issues)
{
    public int OnlineDevices => ActivatedDevices;
    public bool AllOnline => TotalDevices > 0 && ActivatedDevices == TotalDevices && Issues.Count == 0;

    public static RuntimeActivationReport Empty { get; } = new(0, 0, Array.Empty<DeviceActivationIssue>());
}
