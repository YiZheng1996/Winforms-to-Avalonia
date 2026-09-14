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
/// 模板中的一个设备目标。导出器只使用客户可见的设备名称、编码和驱动能力。
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
