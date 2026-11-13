namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 驱动无关的点位地址占用范围。只在校验期间使用，不写入配置文件。
/// </summary>
public sealed record DevicePointAddressRange(string Area, int Offset, int Length);
