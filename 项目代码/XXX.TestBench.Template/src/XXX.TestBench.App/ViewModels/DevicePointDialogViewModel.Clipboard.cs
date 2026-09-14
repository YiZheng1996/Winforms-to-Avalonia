using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class DevicePointDialogViewModel
{
    private string _preservedAddress = string.Empty;
    private PointAddressDefinition? _preservedAddressDefinition;
    private DecodeOptions _preservedDecodeOptions = new();
    private PointWritePolicy _preservedWritePolicy = PointWritePolicy.ReadBackEqual;
    private WriteRiskLevel _preservedRiskLevel = WriteRiskLevel.Normal;

    /// <summary>
    /// 用复制/剪切草稿预填编辑器；稳定 PointId 不复用，保存时仍生成新身份。
    /// </summary>
    public DevicePointDialogViewModel(
        DevicePointPasteDraft draft,
        DevicePointEditContext context)
        : this(false, null, context)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _pointId = string.Empty;
        Code = string.Empty;
        Name = draft.Snapshot.Name;
        Address = draft.Snapshot.Address;
        DataType = draft.Snapshot.DataTypeKind;
        SelectedDataTypeOption = DataTypeOptions.FirstOrDefault(option => option.Value == DataType)
            ?? DataTypeOptions.FirstOrDefault();
        RawMinText = Format(draft.Snapshot.EffectiveRawMin);
        RawMaxText = Format(draft.Snapshot.EffectiveRawMax);
        EngMinText = Format(draft.Snapshot.EffectiveEngMin);
        EngMaxText = Format(draft.Snapshot.EffectiveEngMax);
        IsWritable = draft.Snapshot.IsWritable;
        SelectedRiskLevel = draft.Snapshot.IsWritable ? draft.Snapshot.RiskLevel : WriteRiskLevel.Normal;
        Description = draft.Snapshot.Description;
        _preservedAddress = draft.Snapshot.Address ?? string.Empty;
        _preservedAddressDefinition = CloneAddress(draft.Snapshot.AddressDefinition);
        _preservedDecodeOptions = CloneDecode(draft.Snapshot.DecodeOptions);
        _preservedWritePolicy = draft.Snapshot.WritePolicy;
        _preservedRiskLevel = SelectedRiskLevel;
        SelectedWritePolicy = FindWritePolicy(IsWritable, SelectedRiskLevel);
        // 粘贴入口没有把源点位作为 base 构造函数的 current 参数传入，
        // 因而必须在字段覆盖完成后重新解析 Modbus 的区域、偏移和字节/字序。
        // 否则粘贴一个 HR:0 点位时，弹窗仍可能显示新建默认区域。
        InitializeModbusFields(draft.Snapshot);
        DialogTitle = "粘贴设备点位";
        DialogSubtitle = draft.Mode == DevicePointClipboardMode.Cut
            ? "确认后只调整同一个点位的所属分组，不创建副本"
            : "确认点位名称、地址和分组后创建新点位，不复用源 PointId";
    }

    private static PointAddressDefinition? CloneAddress(PointAddressDefinition? source)
        => source is null
            ? null
            : new PointAddressDefinition
            {
                Area = source.Area,
                Offset = source.Offset,
                BitIndex = source.BitIndex,
                DbNumber = source.DbNumber,
                ByteOffset = source.ByteOffset,
                BitOffset = source.BitOffset,
                LogicalAddress = source.LogicalAddress
            };

    private static DecodeOptions CloneDecode(DecodeOptions? source)
        => source is null
            ? new DecodeOptions()
            : new DecodeOptions
            {
                ByteOrder = source.ByteOrder,
                WordOrder = source.WordOrder
            };
}
