namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 设备点位定义（当前由 points.json 提供；编辑/导入仍保持该运行时边界）。
/// </summary>
public sealed record DevicePoint(
    string Code,
    string Protocol,
    string Address,
    string DataType,
    string Unit,
    bool IsWritable,
    WriteRiskLevel RiskLevel,
    decimal? RawMin,
    decimal? RawMax,
    decimal? EngMin,
    decimal? EngMax,
    string Name = "",
    bool IsEnabled = true,
    string Description = "");
