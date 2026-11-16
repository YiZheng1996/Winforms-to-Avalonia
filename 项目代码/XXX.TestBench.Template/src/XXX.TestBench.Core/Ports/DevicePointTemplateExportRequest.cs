using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 设备点位模板导出的客户范围。内部 Id 只用于本次调用匹配设备，不写入模板。
/// </summary>
public sealed record DevicePointTemplateExportRequest(
    string ScopeDisplayName,
    string ScopeDescription,
    IReadOnlyList<DevicePointTemplateTarget> Targets,
    IReadOnlyList<string> GroupCodes,
    string? DefaultGroupCode = null);

/// <summary>
/// 模板中的一个设备目标。调用方应按设备协议和型号提供有效的数据类型集合；
/// 导出器用该集合生成范围下拉项并集，不会重新推断特定型号能力。
/// </summary>
public sealed record DevicePointTemplateTarget(
    string DeviceId,
    string DeviceCode,
    string DeviceName,
    DevicePointProtocol Protocol,
    string ProtocolDisplayName,
    IReadOnlySet<DevicePointDataType> SupportedDataTypes,
    string Model = "")
{
    /// <summary>
    /// 设备的客户可读名称；设备名称缺失时才回退为编码。
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(DeviceName)
        ? DeviceCode
        : DeviceName;

    /// <summary>
    /// 模板示例使用的设备型号提示；不写入导入文件。
    /// </summary>
    public string ProfileText => Model;
}
