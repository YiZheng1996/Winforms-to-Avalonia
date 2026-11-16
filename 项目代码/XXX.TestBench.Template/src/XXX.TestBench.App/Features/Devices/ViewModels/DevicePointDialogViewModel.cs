using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备点位编辑弹窗提交结果。运行时协议逻辑不在此处创建，只编辑声明式元数据。
/// </summary>
public sealed record DevicePointDialogResult(
    string Code,
    string Name,
    DevicePointProtocol Protocol,
    string Address,
    DevicePointDataType DataType,
    decimal? RawMin,
    decimal? RawMax,
    decimal? EngMin,
    decimal? EngMax,
    bool IsWritable,
    WriteRiskLevel RiskLevel,
    string Description,
    string PointId = "",
    string DeviceId = "",
    string DeviceCode = "",
    string GroupId = "",
    PointAddressDefinition? AddressDefinition = null,
    DecodeOptions? S7DecodeOptions = null,
    PointWritePolicy WritePolicy = PointWritePolicy.ReadBackEqual,
    bool S7OptimizedBlockAccessConfirmed = false)
{
    /// <summary>
    /// 把弹窗结果转换为可保存的点位配置。
    /// </summary>
    public PointsConfig.PointEntry ToEntry() => new()
    {
        Code = Code.Trim(),
        Name = string.IsNullOrWhiteSpace(Name) ? Code.Trim() : Name.Trim(),
        Protocol = DevicePointTypeCatalog.ToStorage(Protocol),
        Id = PointId.Trim(),
        DeviceId = DeviceId.Trim(),
        DeviceCode = DeviceCode.Trim(),
        GroupId = GroupId.Trim(),
        Address = Address.Trim(),
        AddressDefinition = AddressDefinition,
        DataType = DevicePointTypeCatalog.ToStorage(DataType),
        S7DecodeOptions = S7DecodeOptions,
        WritePolicy = WritePolicy,
        RawMin = RawMin,
        RawMax = RawMax,
        EngMin = EngMin,
        EngMax = EngMax,
        IsWritable = IsWritable,
        RiskLevel = RiskLevel,
        Description = Description.Trim()
    };
}

/// <summary>
/// 点位编辑器的所属设备选项。驱动键来自设备配置，点位页面不再自行选择真实协议。
/// </summary>
public sealed record DevicePointDeviceChoice(
    string Id,
    string Code,
    string Name,
    string DriverKey,
    DeviceMode DeviceMode,
    string Model = "",
    string AddressWatermark = "",
    string AddressHint = "",
    int? ModbusUnitId = null,
    ModbusDataOrderOptions? ModbusDataOrder = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Code : Name;
    public string CodeText => string.IsNullOrWhiteSpace(Code) ? string.Empty : $"设备编码：{Code}";
    public string ModeText => DeviceMode == DeviceMode.Simulation ? "仿真模式" : "硬件模式";
    public string StatusText => ModeText;
    public string ProtocolText => DevicePointTypeCatalog.TryParseDriverKey(DriverKey, out var protocol)
        ? DevicePointTypeCatalog.ToDisplayName(protocol)
        : "未识别通信方式";
    public string ModbusUnitIdText => DeviceStationDisplay.FormatValue(DriverKey, ModbusUnitId);
    public string StationText => DeviceStationDisplay.FormatLabel(DriverKey, ModbusUnitId);
    public string ModbusDataOrderText => ModbusDataOrder is { } order
        ? $"{FormatByteOrder(order.ByteOrder)} · {FormatWordOrder(order.WordOrder)}"
        : "未配置";
    public string SummaryText => $"{ProtocolText} · {ModeText}"
        + (string.IsNullOrWhiteSpace(StationText) ? string.Empty : $" · {StationText}");

    private static string FormatByteOrder(ByteOrder value) => value switch
    {
        ByteOrder.BigEndian => "大端模式",
        ByteOrder.LittleEndian => "小端模式",
        _ => "未配置"
    };

    private static string FormatWordOrder(WordOrder value) => value switch
    {
        WordOrder.HighWordFirst => "高字在前",
        WordOrder.LowWordFirst => "低字在前",
        _ => "未配置"
    };
    public override string ToString() => DisplayName;
}

/// <summary>
/// 点位编辑器中的所属分组选项。选项只来自当前设备的既有分组。
/// </summary>
public sealed record DevicePointGroupChoice(
    string Id,
    string DeviceId,
    string Code,
    string Name,
    int SortOrder)
{
    public string DisplayName => string.Equals(Code, "DEFAULT", StringComparison.OrdinalIgnoreCase)
        ? string.Empty
        : string.IsNullOrWhiteSpace(Name) ? Code : Name;
    public string CodeText => string.Equals(Code, "DEFAULT", StringComparison.OrdinalIgnoreCase)
        ? string.Empty
        : $"分组编码：{Code}";
    public string SummaryText => string.Equals(Code, "DEFAULT", StringComparison.OrdinalIgnoreCase)
        ? string.Empty
        : "用于界面分类和筛选";
    public override string ToString() => DisplayName;
}

/// <summary>
/// 简化点位弹窗的业务上下文。真实设备、驱动和可选数据类型由页面注入，
/// 弹窗不再让客户重复选择协议或编辑稳定身份字段。
/// </summary>
public sealed record DevicePointEditContext(
    string ChannelName,
    DevicePointDeviceChoice Device,
    IReadOnlySet<DevicePointDataType> AllowedDataTypes,
    IReadOnlyList<DevicePointGroupChoice> Groups,
    DevicePointGroupChoice? CurrentGroup,
    bool GroupLocked)
{
    /// <summary>
    /// 点位继承的设备轮询周期。点位层不重复维护该值。
    /// </summary>
    public string PollIntervalText { get; init; } = "未配置";

    public string DevicePath
        => string.IsNullOrWhiteSpace(Device.StationText)
            ? $"{ChannelName} / {Device.DisplayName}"
            : $"{ChannelName} / {Device.DisplayName}（{Device.StationText}）";
}

/// <summary>
/// 点位编辑器中的数据类型选项，业务层使用枚举而不是字符串。
/// </summary>
public sealed record DevicePointDataTypeChoice(
    DevicePointDataType Value,
    string DisplayName,
    string Description)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Modbus 地址区域的客户可读选项。值使用 ModbusArea，显示文本不参与协议解析。
/// </summary>
public sealed record ModbusAreaChoice(ModbusArea Value, string DisplayName, string Description)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 点位访问权限选项。客户界面只区分“只读”和“读写”；风险等级作为运行时安全属性保留。
/// </summary>
public sealed record DevicePointWritePolicy(
    string Key,
    string DisplayName,
    string Description,
    bool IsWritable,
    WriteRiskLevel RiskLevel)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 设备点位新增/编辑表单。必填字段和量程关系在提交时再次校验。
/// </summary>
public sealed partial class DevicePointDialogViewModel : ObservableObject
{
    private DevicePointEditContext? _simplifiedContext;
    private bool _groupLocked;
    private IReadOnlySet<DevicePointDataType> _allowedDataTypes = new HashSet<DevicePointDataType>();
    private IReadOnlyList<DevicePointDataTypeChoice> _driverDataTypeOptions = Array.Empty<DevicePointDataTypeChoice>();
    private bool _clipboardPaste;
    private bool _sourceContainsScale;
    private bool _sourceScaleWasPartial;
    private bool _scaleBecameIncompatible;
    private bool _suppressModbusAddressEditTracking;
    private bool _modbusStructureWasEdited;

    /// <summary>
    /// 当前点位弹窗入口：设备、通信方式和分组选项全部来自页面上下文。
    /// </summary>
    public DevicePointDialogViewModel(
        bool isEdit,
        PointsConfig.PointEntry? current,
        DevicePointEditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IsEdit = isEdit;
        _simplifiedContext = context;
        // 先保留“驱动允许的完整类型集合”。Modbus 区域变化时，
        // DataTypeOptions 会按 Coil/DI 或 HR/IR 动态收窄；如果只保留
        // 当前区域的集合，之后从寄存器切换到线圈就无法恢复 Bool 选项。
        _driverDataTypeOptions = DataTypeOptions.ToList();
        _groupLocked = context.GroupLocked || isEdit;
        _allowedDataTypes = context.AllowedDataTypes is { Count: > 0 }
            ? context.AllowedDataTypes
            : new HashSet<DevicePointDataType>();
        DeviceOptions.Add(context.Device);
        foreach (var group in context.Groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase))
            GroupEntries.Add(ToGroupEntry(group));
        DialogTitle = isEdit ? "编辑设备点位" : "新增设备点位";
        DialogSubtitle = isEdit
            ? "修改点位信息后保存；地址和类型按所属设备通信方式校验"
            : "填写点位名称、地址和数据类型，点位保存后立即参与运行";
        SelectedDeviceOption = context.Device;
        _deviceId = context.Device.Id;
        DeviceCode = context.Device.Code;

        if (current is null)
        {
            RefreshGroupOptions(context.CurrentGroup?.Id);
            Protocol = ProtocolFromDriver(context.Device.DriverKey);
            DataType = DevicePointDataType.Decimal;
            SelectedDataTypeOption = DataTypeOptions.FirstOrDefault(option => option.Value == DataType)
                ?? DataTypeOptions.FirstOrDefault();
            SelectedRiskLevel = WriteRiskLevel.Normal;
            SelectedWritePolicy = WritePolicyOptions[0];
        }
        else
        {
            Code = current.Code;
            Name = current.Name;
            _pointId = current.Id;
            _pendingGroupId = current.GroupId;
            RefreshGroupOptions(current.GroupId);
            PointTag = DevicePointTag.Format(
                GroupOptions.FirstOrDefault(option =>
                    string.Equals(option.Id, current.GroupId, StringComparison.OrdinalIgnoreCase))?.Code
                ?? current.GroupCode,
                string.IsNullOrWhiteSpace(current.Name) ? current.Code : current.Name);
            Protocol = ProtocolFromDriver(context.Device.DriverKey);
            Address = current.Address;
            DataType = DevicePointTypeCatalog.TryParseDataType(current.DataType, out var dataType)
                ? dataType
                : DevicePointDataType.Unknown;
            RawMinText = Format(current.RawMin);
            RawMaxText = Format(current.RawMax);
            EngMinText = Format(current.EngMin);
            EngMaxText = Format(current.EngMax);
            IsWritable = current.IsWritable;
            SelectedRiskLevel = current.RiskLevel;
            Description = current.Description;
            // 旧配置中的高风险等级不再作为第三个访问权限选项展示，但编辑后仍需保留。
            SelectedWritePolicy = FindWritePolicy(IsWritable, SelectedRiskLevel);
            SelectedRiskLevel = current.IsWritable ? current.RiskLevel : WriteRiskLevel.Normal;
            _preservedAddress = current.Address ?? string.Empty;
            _preservedAddressDefinition = current.AddressDefinition;
            _preservedS7DecodeOptions = current.S7DecodeOptions;
            _preservedWritePolicy = current.WritePolicy;
        }

        InitializeModbusFields(current);

        ApplySimplifiedContext(context, current);
    }

    private void ApplySimplifiedContext(
        DevicePointEditContext context,
        PointsConfig.PointEntry? current)
    {
        var supported = context.AllowedDataTypes ?? new HashSet<DevicePointDataType>();
        if (supported.Count > 0)
        {
            var allowed = _driverDataTypeOptions
                .Where(option => supported.Contains(option.Value))
                .ToList();
            DataTypeOptions.Clear();
            foreach (var option in allowed)
                DataTypeOptions.Add(option);

            var currentType = current is not null
                && DevicePointTypeCatalog.TryParseDataType(current.DataType, out var parsedCurrentType)
                    ? parsedCurrentType
                    : DevicePointDataType.Unknown;
            var targetType = current is null
                ? IsModbus
                    ? (IsModbusBitArea
                        ? DevicePointDataType.Bool
                        : DevicePointDataType.Int16)
                    : DataType
                : currentType switch
                {
                    DevicePointDataType.Decimal => DevicePointDataType.Double,
                    DevicePointDataType.Bool => DevicePointDataType.Bool,
                    DevicePointDataType.String => DevicePointDataType.String,
                    _ => currentType
                };

            if (current is not null
                && (targetType == DevicePointDataType.Unknown || !supported.Contains(targetType)))
            {
                DataType = DevicePointDataType.Unknown;
                SelectedDataTypeOption = null;
            }
            else
            {
                SelectedDataTypeOption = DataTypeOptions.FirstOrDefault(option => option.Value == targetType)
                    ?? DataTypeOptions.FirstOrDefault();
                DataType = SelectedDataTypeOption?.Value ?? DevicePointDataType.Unknown;
            }
        }

        // 驱动能力过滤完成后，再按当前 Modbus 数据区过滤一次。
        // 这样“线圈/离散输入只能选 Bool，寄存器只能选数值类型”的规则
        // 在下拉框层就可见，提交时仍由 ModbusTypeCapabilities 再校验一次。
        RefreshModbusDataTypeOptions();

        if (current is null && context.CurrentGroup is not null)
            SelectedGroupOption = GroupOptions.FirstOrDefault(option =>
                string.Equals(option.Id, context.CurrentGroup.Id, StringComparison.OrdinalIgnoreCase));
        InitializeScalingFromPoint(current);
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(CurrentGroupText));
        OnPropertyChanged(nameof(UseGroupSelection));
        OnPropertyChanged(nameof(IsGroupLocked));
        OnPropertyChanged(nameof(ShowGroupSelection));
        OnPropertyChanged(nameof(ShowCurrentGroup));
        OnPropertyChanged(nameof(CanSave));
        NotifyModbusChanged();
    }

    private static PointsConfig.PointGroupEntry ToGroupEntry(DevicePointGroupChoice group)
        => new()
        {
            Id = group.Id,
            DeviceId = group.DeviceId,
            Code = group.Code,
            Name = group.Name,
            SortOrder = group.SortOrder
        };

    /// <summary>
    /// 是否为编辑模式。
    /// </summary>
    public bool IsEdit { get; }
    /// <summary>
    /// 弹窗标题。
    /// </summary>
    public string DialogTitle { get; }
    /// <summary>
    /// 弹窗说明文字。
    /// </summary>
    public string DialogSubtitle { get; }
    /// <summary>
    /// 当前可选择的设备。
    /// </summary>
    public ObservableCollection<DevicePointDeviceChoice> DeviceOptions { get; } = new();

    private readonly List<PointsConfig.PointGroupEntry> GroupEntries = new();

    /// <summary>
    /// 当前所选设备可用的点位分组，切换设备后会重新生成。
    /// </summary>
    public ObservableCollection<DevicePointGroupChoice> GroupOptions { get; } = new();

    public ObservableCollection<ModbusAreaChoice> ModbusAreaOptions { get; } =
    [
        new(ModbusArea.Coil, "线圈", "可读写；功能码 01/05；布尔量"),
        new(ModbusArea.DiscreteInput, "离散输入", "只读；功能码 02；布尔量"),
        new(ModbusArea.HoldingRegister, "保持寄存器", "可读写；功能码 03/06/16；数值类型"),
        new(ModbusArea.InputRegister, "输入寄存器", "只读；功能码 04；数值类型")
    ];

    public bool IsStructuredModbusAddressVisible => IsModbus && !IsXinjeFieldV1;
    public bool IsStandardAddressVisible => !IsModbus || IsXinjeFieldV1;
    public bool IsGroupLocked => _groupLocked;
    public bool UseGroupSelection => !_groupLocked;
    public bool ShowGroupSelection => UseGroupSelection && GroupOptions.Count > 0;
    public bool ShowCurrentGroup => IsGroupLocked && !string.IsNullOrWhiteSpace(CurrentGroupText);
    public string ChannelName => _simplifiedContext?.ChannelName ?? string.Empty;
    public string PathText
    {
        get
        {
            var group = IsGroupLocked
                ? _simplifiedContext!.CurrentGroup
                : SelectedGroupOption;
            return group is null || string.IsNullOrWhiteSpace(group.DisplayName)
                ? _simplifiedContext!.DevicePath
                : $"{_simplifiedContext!.DevicePath} / {group.DisplayName}";
        }
    }
    public string PollIntervalText => _simplifiedContext!.PollIntervalText;
    public string CurrentGroupText => _simplifiedContext!.CurrentGroup?.DisplayName ?? string.Empty;
    public bool IsModbus => Protocol is DevicePointProtocol.ModbusRtu or DevicePointProtocol.ModbusTcp;
    public bool IsXinjeFieldV1 => IsModbus
        && ModbusPointAddressResolver.IsXinjeFieldV1(SelectedDeviceOption?.Model);
    public bool IsModbusBitArea => IsModbus
        && TryGetModbusAddress(out var address, out _)
        && address.Area.IsBitArea();
    public bool IsModbusRegisterArea => IsModbus
        && TryGetModbusAddress(out var address, out _)
        && !address.Area.IsBitArea();
    public bool IsModbusWritableArea => IsModbus
        && TryGetModbusAddress(out var address, out _)
        && address.Area.IsWritableArea();
    public bool IsModbusReadOnlyArea => IsModbus
        && TryGetModbusAddress(out var address, out _)
        && !address.Area.IsWritableArea();
    public bool IsModbusAccessSelectionEnabled => !IsModbus || !IsModbusReadOnlyArea;
    public string ModbusDataOrderText => SelectedDeviceOption?.ModbusDataOrderText ?? "未配置";
    public string ModbusCanonicalAddress
        => IsXinjeFieldV1 && TryGetModbusAddress(out var nativeAddress, out _)
            ? $"换算后的 Modbus 地址：{nativeAddress.Canonical}"
            : SelectedModbusArea is { } area
            && int.TryParse(ModbusOffsetText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
            && offset is >= 0 and <= ushort.MaxValue
            ? $"已生成地址：{area.DisplayName} {offset}"
            : "请填写 0-65535 的零基偏移";
    public string ModbusAddressHint
        => "系统会按所选数据区和零基偏移生成通信地址，不根据裸数字猜测功能码。";
    public bool IsScaleVisible => IsNumericDataType;
    public bool IsNumericDataType => DataType is DevicePointDataType.Decimal
        or DevicePointDataType.Byte
        or DevicePointDataType.Int16
        or DevicePointDataType.Int32
        or DevicePointDataType.UInt16
        or DevicePointDataType.UInt32
        or DevicePointDataType.Float32
        or DevicePointDataType.Double;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(IsScaleVisible))]
    private bool _isScalingEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(DeviceHelpText))]
    [NotifyPropertyChangedFor(nameof(ModbusDataOrderText))]
    private DevicePointDeviceChoice? _selectedDeviceOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(GroupHelpText))]
    private DevicePointGroupChoice? _selectedGroupOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(IsModbusBitArea))]
    [NotifyPropertyChangedFor(nameof(IsModbusRegisterArea))]
    [NotifyPropertyChangedFor(nameof(IsModbusWritableArea))]
    [NotifyPropertyChangedFor(nameof(ModbusCanonicalAddress))]
    private ModbusAreaChoice? _selectedModbusArea;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ModbusCanonicalAddress))]
    private string _modbusOffsetText = string.Empty;

    /// <summary>
    /// 所属设备选择说明。
    /// </summary>
    public string DeviceHelpText => SelectedDeviceOption is null
        ? "点位必须归属于已配置设备；此处不直接创建设备。"
        : $"通信方式：{SelectedDeviceOption.ProtocolText}；{SelectedDeviceOption.StatusText}。"
          + (string.IsNullOrWhiteSpace(SelectedDeviceOption.StationText)
              ? string.Empty
              : $"{SelectedDeviceOption.StationText}。")
          + (IsModbus ? $"数据排列：{ModbusDataOrderText}，由设备统一维护。" : string.Empty)
          + "地址和类型将由该通信方式最终验证，采集周期在设备配置中统一维护。";

    /// <summary>
    /// 所属分组只用于树状组织和筛选，不参与地址解析或运行时路由。
    /// </summary>
    public string GroupHelpText => SelectedGroupOption is null
        ? "可选：分组只用于树状分类和筛选。"
        : "分组只改变树状界面归类，不会改变点位地址、驱动或实时值。";

    /// <summary>
    /// 可选的数据类型，显示中文含义并保留驱动使用的内部值。
    /// </summary>
    public ObservableCollection<DevicePointDataTypeChoice> DataTypeOptions { get; } =
    [
        new(DevicePointDataType.Char, "字符", "单个字符，驱动按设备协议编码"),
        new(DevicePointDataType.Byte, "字节", "一个字节的无符号整数，范围 0～255"),
        new(DevicePointDataType.Int16, "短整型", "两个字节的有符号整数"),
        new(DevicePointDataType.UInt16, "字", "两个字节的无符号整数"),
        new(DevicePointDataType.Int32, "长整型", "四个字节的有符号整数"),
        new(DevicePointDataType.UInt32, "双字", "四个字节的无符号整数"),
        new(DevicePointDataType.Float32, "浮点型", "四个字节的 IEEE 754 单精度浮点数"),
        new(DevicePointDataType.Double, "双精度", "八个字节的 IEEE 754 双精度浮点数"),
        new(DevicePointDataType.String, "字符串", "设备返回或需要写入的一段文字"),
        new(DevicePointDataType.Bool, "布尔量", "只有真/假或开/关两种状态")
    ];

    /// <summary>
    /// 可选的访问权限。高风险含义不是第三种权限，而是读写点位的运行时安全属性。
    /// </summary>
    public ObservableCollection<DevicePointWritePolicy> WritePolicyOptions { get; } =
    [
        new("ReadOnly", "只读", "只采集和显示数据，不允许人工写入", false, WriteRiskLevel.Normal),
        new("ReadWrite", "读写", "允许在受控流程中写入；高风险动作仍需更高权限和二次确认", true, WriteRiskLevel.Normal)
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private DevicePointDataTypeChoice? _selectedDataTypeOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private DevicePointWritePolicy? _selectedWritePolicy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    /// <summary>
    /// 客户可读的点位标签输入；保存时自动拆分为内部编码和名称。
    /// </summary>
    private string _pointTag = string.Empty;

    /// <summary>
    /// 系统保留的点位编码；新界面不要求客户直接输入。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    /// <summary>
    /// 点位名称输入。
    /// </summary>
    private string _name = string.Empty;

    /// <summary>
    /// 弹窗中的客户可读点位名称。
    /// </summary>
    public string PointName
    {
        get => Name;
        set => Name = value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(AddressHint))]
    [NotifyPropertyChangedFor(nameof(AddressWatermark))]
    /// <summary>
    /// 协议输入。
    /// </summary>
    private DevicePointProtocol _protocol = DevicePointProtocol.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    /// <summary>
    /// 地址输入。
    /// </summary>
    private string _address = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    /// <summary>
    /// 数据类型输入。
    /// </summary>
    private DevicePointDataType _dataType = DevicePointDataType.Unknown;

    [ObservableProperty]
    /// <summary>
    /// 原始下限输入文字。
    /// </summary>
    private string _rawMinText = string.Empty;

    [ObservableProperty]
    /// <summary>
    /// 原始上限输入文字。
    /// </summary>
    private string _rawMaxText = string.Empty;

    [ObservableProperty]
    /// <summary>
    /// 工程下限输入文字。
    /// </summary>
    private string _engMinText = string.Empty;

    [ObservableProperty]
    /// <summary>
    /// 工程上限输入文字。
    /// </summary>
    private string _engMaxText = string.Empty;

    [ObservableProperty]
    /// <summary>
    /// 是否允许写入。
    /// </summary>
    private bool _isWritable;

    [ObservableProperty]
    /// <summary>
    /// 选中的风险等级。
    /// </summary>
    private WriteRiskLevel _selectedRiskLevel;

    private string _pointId = string.Empty;
    private string _deviceId = string.Empty;
    private string _pendingGroupId = string.Empty;

    [ObservableProperty]
    private string _deviceCode = string.Empty;

    [ObservableProperty]
    /// <summary>
    /// 点位说明输入。
    /// </summary>
    private string _description = string.Empty;

    /// <summary>
    /// 当前数据类型的解释。
    /// </summary>
    public string DataTypeHelpText => SelectedDataTypeOption?.Description
        ?? "系统会按照所选类型解析设备返回值，并校验需要写入的值。";

    /// <summary>
    /// 输入框中的短地址示例，会随所属设备通信方式变化。
    /// </summary>
    public string AddressWatermark => IsXinjeFieldV1
        ? SelectedDeviceOption?.AddressWatermark ?? "如：D100、HD1000、M400、HM40"
        : IsModbus
        ? "请选择数据区并填写零基偏移"
        : !string.IsNullOrWhiteSpace(SelectedDeviceOption?.AddressWatermark)
        ? SelectedDeviceOption.AddressWatermark
        : Protocol switch
        {
            DevicePointProtocol.Simulation => "如：sim.pressure",
            DevicePointProtocol.SiemensS7 => "如：DB144.DBD88 或 VW5022",
            _ => "按设备手册填写"
        };

    /// <summary>
    /// 当前通信方式对应的详细地址填写提示。
    /// </summary>
    public string AddressHint => IsXinjeFieldV1
        ? SelectedDeviceOption?.AddressHint ?? "仅支持现场已验证的 D/HD/M/HM；不支持寄存器位地址。"
        : IsModbus
        ? ModbusAddressHint
        : !string.IsNullOrWhiteSpace(SelectedDeviceOption?.AddressHint)
        ? SelectedDeviceOption.AddressHint
        : Protocol switch
        {
            DevicePointProtocol.Simulation =>
                "仿真地址示例：sim.pressure；命令点位可用 sim.start。仿真地址不参与 PLC 寄存器解析。",
            DevicePointProtocol.SiemensS7 =>
                "西门子 S7 地址格式由所选设备系列决定。",
            _ => "请先确认所属设备的通信方式，再按设备手册填写地址。"
        };

    public bool HasAddress => IsModbus
        ? TryGetModbusAddress(out _, out _)
        : !string.IsNullOrWhiteSpace(Address);

    /// <summary>
    /// 当前访问权限对应的安全提示。
    /// </summary>
    public string WriteSafetyMessage => !IsWritable
        ? "只读点位只采集和显示数据，不允许人工写入。"
        : SelectedRiskLevel == WriteRiskLevel.HighRisk
            ? "该读写点位属于高风险动作，运行时需要更高权限和二次确认。"
            : SelectedWritePolicy?.Description
                ?? "读写点位只会在受控流程中写入；保存此设置不会立即向设备写入数据。";

    partial void OnAddressChanged(string value)
    {
        if (IsModbus)
            RefreshModbusDataTypeOptions();
        NotifyModbusChanged();
        OnPropertyChanged(nameof(HasAddress));
        NotifyS7OptimizedBlockAccessChanged();
    }

    partial void OnProtocolChanged(DevicePointProtocol value)
    {
        // 设备切换时重新计算 Modbus 专属控件的可见性；结构化字段只在
        // 当前设备确实是 Modbus 时参与保存，避免把旧设备的地址语义带到新设备。
        if (value is DevicePointProtocol.ModbusRtu or DevicePointProtocol.ModbusTcp)
            InitializeModbusFields(null);
        RefreshModbusDataTypeOptions();
        NotifyModbusChanged();
        OnPropertyChanged(nameof(AddressHint));
        OnPropertyChanged(nameof(AddressWatermark));
        OnPropertyChanged(nameof(HasAddress));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnSelectedDeviceOptionChanged(DevicePointDeviceChoice? value)
    {
        Protocol = ProtocolFromDriver(value?.DriverKey);
        _deviceId = value?.Id ?? string.Empty;
        DeviceCode = value?.Code ?? string.Empty;
        RefreshGroupOptions(_pendingGroupId);
        _pendingGroupId = string.Empty;
        OnPropertyChanged(nameof(DeviceHelpText));
        OnPropertyChanged(nameof(AddressHint));
        OnPropertyChanged(nameof(AddressWatermark));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(CanSave));
        NotifyS7OptimizedBlockAccessChanged();
        NotifyModbusChanged();
        RefreshModbusDataTypeOptions();
    }

    private void NotifyS7OptimizedBlockAccessChanged()
    {
        OnPropertyChanged(nameof(S7OptimizedBlockAccessNotice));
        OnPropertyChanged(nameof(HasS7OptimizedBlockAccessHint));
        OnPropertyChanged(nameof(RequiresS7OptimizedBlockAccessConfirmation));
        OnPropertyChanged(nameof(S7OptimizedBlockAccessShortMessage));
        OnPropertyChanged(nameof(S7OptimizedBlockAccessDetailMessage));
    }

    partial void OnSelectedGroupOptionChanged(DevicePointGroupChoice? value)
    {
        OnPropertyChanged(nameof(GroupHelpText));
        OnPropertyChanged(nameof(CurrentGroupText));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(ShowCurrentGroup));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnSelectedDataTypeOptionChanged(DevicePointDataTypeChoice? value)
    {
        DataType = value?.Value ?? DevicePointDataType.Unknown;
        if (_sourceContainsScale)
        {
            _scaleBecameIncompatible = !IsNumericDataType;
            IsScalingEnabled = !_sourceScaleWasPartial && IsNumericDataType;
        }
        if (!IsNumericDataType)
        {
            IsScalingEnabled = false;
            if (_sourceContainsScale)
            {
                // 复制/剪切目标改成非数值类型时，不能静默丢弃源点位量程。
                _scaleBecameIncompatible = true;
            }
            else
            {
                RawMinText = string.Empty;
                RawMaxText = string.Empty;
                EngMinText = string.Empty;
                EngMaxText = string.Empty;
            }
        }
        OnPropertyChanged(nameof(DataTypeHelpText));
        OnPropertyChanged(nameof(IsNumericDataType));
        OnPropertyChanged(nameof(IsScaleVisible));
        NotifyModbusChanged();
    }

    partial void OnSelectedWritePolicyChanged(DevicePointWritePolicy? value)
    {
        if (value is not null)
        {
            IsWritable = value.IsWritable;
            SelectedRiskLevel = value.RiskLevel;
        }

        OnPropertyChanged(nameof(WriteSafetyMessage));
        NotifyModbusChanged();
    }

    partial void OnSelectedModbusAreaChanged(ModbusAreaChoice? value)
    {
        if (!_suppressModbusAddressEditTracking)
            _modbusStructureWasEdited = true;
        // DiscreteInput/InputRegister 没有写功能码。切换到只读区时，
        // 立即把访问权限归一化为只读，避免界面继续显示一个最终必然失败的“读写”。
        if (IsModbusReadOnlyArea && SelectedWritePolicy?.IsWritable == true)
            SelectedWritePolicy = WritePolicyOptions[0];

        RefreshModbusDataTypeOptions();
        OnPropertyChanged(nameof(ModbusCanonicalAddress));
        OnPropertyChanged(nameof(IsModbusBitArea));
        OnPropertyChanged(nameof(IsModbusRegisterArea));
        OnPropertyChanged(nameof(IsModbusWritableArea));
        OnPropertyChanged(nameof(IsModbusReadOnlyArea));
        OnPropertyChanged(nameof(IsModbusAccessSelectionEnabled));
        OnPropertyChanged(nameof(HasAddress));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnModbusOffsetTextChanged(string value)
    {
        if (!_suppressModbusAddressEditTracking)
            _modbusStructureWasEdited = true;
        OnPropertyChanged(nameof(ModbusCanonicalAddress));
        OnPropertyChanged(nameof(HasAddress));
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// 从已有点位或新建默认值初始化 Modbus 的结构化字段。
    ///
    /// Address 仍会保留在配置模型中作为可读兼容文本，但编辑 Modbus 时不再
    /// 让用户在一个自由文本框里同时承担“数据区”和“偏移”两种语义。
    /// </summary>
    private void InitializeModbusFields(PointsConfig.PointEntry? current)
    {
        if (!IsModbus)
        {
            SelectedModbusArea = null;
            ModbusOffsetText = string.Empty;
            return;
        }

        if (IsXinjeFieldV1)
        {
            _suppressModbusAddressEditTracking = true;
            try
            {
                SelectedModbusArea = null;
                ModbusOffsetText = string.Empty;
            }
            finally
            {
                _suppressModbusAddressEditTracking = false;
            }
            NotifyModbusChanged();
            return;
        }

        var address = default(ModbusAddress);
        var hasAddress = current is not null
            && ModbusAddressParser.TryParse(current.Address, out address, out _);
        var selectedArea = hasAddress
            ? ModbusAreaOptions.FirstOrDefault(option => option.Value == address.Area)
            : ModbusAreaOptions.FirstOrDefault(option => option.Value == ModbusArea.HoldingRegister);
        _suppressModbusAddressEditTracking = true;
        try
        {
            SelectedModbusArea = selectedArea;
            ModbusOffsetText = hasAddress
                ? address.Offset.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }
        finally
        {
            _suppressModbusAddressEditTracking = false;
        }

        NotifyModbusChanged();
    }

    /// <summary>
    /// 统一初始化编辑和复制/剪切粘贴的量程状态：只有原始值、工程值四项
    /// 都存在且目标类型为数值型时才打开量程；部分量程和不兼容类型必须留痕，
    /// 由提交校验给出明确错误，不能把源配置悄悄改成“无量程”。
    /// </summary>
    private void InitializeScalingFromPoint(PointsConfig.PointEntry? point)
    {
        _sourceContainsScale = point is not null
            && new[]
            {
                point.RawMin,
                point.RawMax,
                point.EngMin,
                point.EngMax
            }.Any(value => value.HasValue);
        _sourceScaleWasPartial = _sourceContainsScale
            && (point!.RawMin is null
                || point.RawMax is null
                || point.EngMin is null
                || point.EngMax is null);
        _scaleBecameIncompatible = _sourceContainsScale && !IsNumericDataType;
        IsScalingEnabled = _sourceContainsScale
            && !_sourceScaleWasPartial
            && !_scaleBecameIncompatible
            && IsNumericDataType;
    }

    /// <summary>
    /// 读取表单中的区域和零基偏移，并重新通过统一解析器确认范围。
    /// </summary>
    private bool TryGetModbusAddress(out ModbusAddress address, out string error)
    {
        address = default;
        if (!IsModbus)
        {
            error = "当前点位不是 Modbus 点位";
            return false;
        }
        if (IsXinjeFieldV1)
        {
            if (!XinjeFieldV1AddressParser.TryParse(Address, out var xinjeAddress, out var xinjeParseError))
            {
                error = xinjeParseError ?? "请输入有效的信捷 D、HD、M 或 HM 地址";
                return false;
            }
            address = xinjeAddress.ModbusAddress;
            if (!IsPreservedAddressDefinitionConsistent(address))
            {
                error = "现有结构化 Modbus 地址与 Address 不一致；请明确修改原生地址后再保存";
                address = default;
                return false;
            }
            error = string.Empty;
            return true;
        }
        if (SelectedModbusArea is null)
        {
            error = "请选择 Modbus 数据区";
            return false;
        }
        if (!int.TryParse(ModbusOffsetText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
        {
            error = "Modbus 地址偏移必须是整数";
            return false;
        }
        if (offset is < 0 or > ushort.MaxValue)
        {
            error = "Modbus 地址偏移必须在 0-65535 范围内";
            return false;
        }

        var candidate = $"{SelectedModbusArea.Value.ToCode()}:{offset}";
        if (!ModbusAddressParser.TryParse(candidate, out address, out var parseError))
        {
            error = parseError ?? "Modbus 地址无法解析";
            return false;
        }
        if (!IsPreservedAddressDefinitionConsistent(address))
        {
            error = "现有结构化 Modbus 地址与 Address 不一致；请明确修改数据区或偏移后再保存";
            address = default;
            return false;
        }
        error = string.Empty;
        return true;
    }

    private bool IsPreservedAddressDefinitionConsistent(ModbusAddress address)
    {
        var definition = _preservedAddressDefinition;
        if (!IsEdit
            || _modbusStructureWasEdited
            || !string.Equals(Address.Trim(), _preservedAddress.Trim(), StringComparison.OrdinalIgnoreCase)
            || definition is null
            || (string.IsNullOrWhiteSpace(definition.Area) && !definition.Offset.HasValue))
            return true;

        return ModbusAreaExtensions.TryParse(definition.Area, out var definitionArea)
            && definition.Offset == address.Offset
            && definitionArea == address.Area;
    }

    /// <summary>
    /// 结构化表单的快速校验，供 CanSave 使用；提交时仍会执行带错误原因的完整校验。
    /// </summary>
    private bool IsModbusFormComplete()
    {
        var order = SelectedDeviceOption?.ModbusDataOrder;
        if (!TryGetModbusAddress(out var address, out _)
            || SelectedDataTypeOption is null
            || order is null)
            return false;

        var type = DataType;
        if (!IsXinjeDataTypeAllowed(type))
            return false;
        var writable = SelectedWritePolicy?.IsWritable ?? IsWritable;
        return ModbusTypeCapabilities.TryValidate(
            address.Area,
            type,
            writable,
            order,
            out _);
    }

    private bool IsXinjeDataTypeAllowed(DevicePointDataType type)
    {
        if (!IsXinjeFieldV1)
            return true;
        if (!XinjeFieldV1AddressParser.TryParse(Address, out var address, out _))
            return Enum.GetValues<XinjeFieldV1Area>()
                .Any(area => ModbusPointAddressResolver.IsSupportedXinjeDataType(area, type));
        return ModbusPointAddressResolver.IsSupportedXinjeDataType(address.Area, type);
    }

    private void NotifyModbusChanged()
    {
        OnPropertyChanged(nameof(IsModbus));
        OnPropertyChanged(nameof(IsXinjeFieldV1));
        OnPropertyChanged(nameof(IsStructuredModbusAddressVisible));
        OnPropertyChanged(nameof(IsStandardAddressVisible));
        OnPropertyChanged(nameof(IsModbusBitArea));
        OnPropertyChanged(nameof(IsModbusRegisterArea));
        OnPropertyChanged(nameof(IsModbusWritableArea));
        OnPropertyChanged(nameof(IsModbusReadOnlyArea));
        OnPropertyChanged(nameof(IsModbusAccessSelectionEnabled));
        OnPropertyChanged(nameof(ModbusCanonicalAddress));
        OnPropertyChanged(nameof(HasAddress));
        OnPropertyChanged(nameof(AddressHint));
        OnPropertyChanged(nameof(AddressWatermark));
        OnPropertyChanged(nameof(ModbusDataOrderText));
        OnPropertyChanged(nameof(DeviceHelpText));
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// 按 Modbus 区域收窄数据类型下拉项。
    ///
    /// 位区只接受 Bool；寄存器区只接受方案定义的 16/32/64 位数值类型。
    /// 不在切换区域时偷偷把旧类型转换成另一个类型：如果旧类型不再适用，
    /// 清空选择并让 CanSave 变为 false，迫使客户明确确认新类型。
    /// </summary>
    private void RefreshModbusDataTypeOptions()
    {
        if (!IsModbus)
            return;

        var source = _driverDataTypeOptions.Count > 0
            ? _driverDataTypeOptions
            : DataTypeOptions.ToList();
        var filtered = source
            .Where(option => _allowedDataTypes.Count == 0 || _allowedDataTypes.Contains(option.Value))
            .Where(option =>
            {
                if (IsXinjeFieldV1)
                {
                    if (XinjeFieldV1AddressParser.TryParse(Address, out var native, out _))
                        return ModbusPointAddressResolver.IsSupportedXinjeDataType(native.Area, option.Value);
                    // 地址未完成时展示 V1 类型并集，但保存仍由解析结果禁止。
                    return Enum.GetValues<XinjeFieldV1Area>()
                        .Any(area => ModbusPointAddressResolver.IsSupportedXinjeDataType(area, option.Value));
                }

                var isBitArea = SelectedModbusArea?.Value.IsBitArea() == true;
                return isBitArea
                    ? option.Value == DevicePointDataType.Bool
                    : option.Value is DevicePointDataType.Int16
                        or DevicePointDataType.UInt16
                        or DevicePointDataType.Int32
                        or DevicePointDataType.UInt32
                        or DevicePointDataType.Float32
                        or DevicePointDataType.Double;
            })
            .ToList();

        DataTypeOptions.Clear();
        foreach (var option in filtered)
            DataTypeOptions.Add(option);

        var selectedValue = SelectedDataTypeOption?.Value ?? DataType;
        var selected = filtered.FirstOrDefault(option => option.Value == selectedValue);
        if (selected is not null)
        {
            if (!ReferenceEquals(SelectedDataTypeOption, selected))
                SelectedDataTypeOption = selected;
        }
        else if (SelectedDataTypeOption is not null || DataType != DevicePointDataType.Unknown)
        {
            SelectedDataTypeOption = null;
            DataType = DevicePointDataType.Unknown;
        }

        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// 当前地址对应的西门子 S7 数据块绝对地址提示；非 S7 DB 绝对地址为空。
    /// </summary>
    public SiemensS7OptimizedBlockAccessNotice? S7OptimizedBlockAccessNotice
        => SiemensS7OptimizedBlockAccessAdvisor.CreateDisplayNotice(
            SelectedDeviceOption?.DriverKey,
            Address);

    /// <summary>
    /// 是否显示地址区内的优化块访问短提示。
    /// </summary>
    public bool HasS7OptimizedBlockAccessHint => S7OptimizedBlockAccessNotice is not null;

    /// <summary>
    /// S7 硬件模式使用 DB 绝对地址时，保存前需要一次客户确认。
    /// </summary>
    public bool RequiresS7OptimizedBlockAccessConfirmation
        => SelectedDeviceOption is not null
            && SiemensS7OptimizedBlockAccessAdvisor.ShouldRequireConfirmation(
                SelectedDeviceOption.DriverKey,
                SelectedDeviceOption.DeviceMode,
                Address);

    public string S7OptimizedBlockAccessShortMessage
        => S7OptimizedBlockAccessNotice?.ShortMessage ?? string.Empty;

    public string S7OptimizedBlockAccessDetailMessage
        => S7OptimizedBlockAccessNotice?.DetailMessage ?? string.Empty;

    private string _validationMessage = string.Empty;
    /// <summary>
    /// 校验失败时展示的提示。
    /// </summary>
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    /// <summary>
    /// 必填项是否齐全，控制保存按钮可用性。
    /// </summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Name)
        && SelectedDeviceOption is not null
        && (!UseGroupSelection || GroupOptions.Count == 0 || SelectedGroupOption is not null || IsEdit)
        && (IsModbus ? IsModbusFormComplete() : !string.IsNullOrWhiteSpace(Address))
        && DataType != DevicePointDataType.Unknown
        && SelectedDataTypeOption is not null
        && SelectedWritePolicy is not null;

    /// <summary>
    /// 校验输入并生成提交结果，失败时给出提示。
    /// </summary>
    public bool TryBuildResult(out DevicePointDialogResult result)
        => TryBuildSimplifiedResult(out result);

    private bool TryBuildSimplifiedResult(out DevicePointDialogResult result)
    {
        var errors = new List<string>();
        if (_simplifiedContext is null)
        {
            errors.Add("点位编辑上下文未初始化");
            result = null!;
            ValidationMessage = string.Join("；", errors);
            return false;
        }

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("点位名称不能为空");
        if (SelectedDeviceOption is null)
            errors.Add("所属设备不能为空");
        ModbusAddress modbusAddress = default;
        var modbusAddressValid = true;
        if (IsModbus)
        {
            modbusAddressValid = TryGetModbusAddress(out modbusAddress, out var modbusAddressError);
            if (!modbusAddressValid)
                errors.Add(modbusAddressError);
        }
        else if (string.IsNullOrWhiteSpace(Address))
        {
            errors.Add("地址不能为空");
        }
        if (SelectedDataTypeOption is null || DataType == DevicePointDataType.Unknown)
            errors.Add("请选择数据类型");
        if (_allowedDataTypes.Count > 0 && !_allowedDataTypes.Contains(DataType))
            errors.Add("当前设备驱动不支持所选数据类型");
        if (SelectedWritePolicy is null)
            errors.Add("请选择访问权限");

        var writable = SelectedWritePolicy?.IsWritable ?? IsWritable;
        DecodeOptions? s7DecodeOptions;
        if (IsModbus)
        {
            var modbusDataOrder = SelectedDeviceOption?.ModbusDataOrder;
            if (modbusDataOrder is null)
                errors.Add("所属 Modbus 设备未配置字节序和字序，请先在设备属性中设置");
            if (modbusAddressValid && DataType != DevicePointDataType.Unknown)
            {
                var modbusType = DataType;
                if (!IsXinjeDataTypeAllowed(modbusType))
                    errors.Add(modbusAddress.Area.IsBitArea()
                        ? "信捷 M/HM 点位只支持 Bool 类型"
                        : "信捷 D/HD 点位只支持 UInt16、Float32、Int32 或 UInt32 类型");
                if (!ModbusTypeCapabilities.TryValidate(
                        modbusAddress.Area,
                        modbusType,
                        writable,
                        modbusDataOrder,
                        out var modbusTypeError))
                {
                    errors.Add(modbusTypeError ?? "当前 Modbus 区域、类型、写权限或设备级数据排列组合无效");
                }
                else if (ModbusTypeCapabilities.TryGetRegisterCount(modbusType, out var registerCount)
                    && modbusAddress.Offset + registerCount > ushort.MaxValue + 1)
                {
                    errors.Add("点位占用的寄存器范围不能超过 65535");
                }
            }
            s7DecodeOptions = null;
        }
        else
        {
            s7DecodeOptions = _preservedS7DecodeOptions;
        }
        var group = _groupLocked
            ? _simplifiedContext.CurrentGroup
            : SelectedGroupOption;
        if (group is null && !_groupLocked && GroupOptions.Count > 0)
            errors.Add("请选择所属分组");

        if (_clipboardPaste && _sourceScaleWasPartial)
            errors.Add("源点位量程配置不完整，复制/剪切粘贴前请补全四个量程值");
        if (_clipboardPaste && _scaleBecameIncompatible)
            errors.Add("源点位包含量程换算，目标数据类型不支持量程，不能静默丢弃");

        var rawMin = IsScalingEnabled ? ReadDecimal(RawMinText, "原始下限", errors) : null;
        var rawMax = IsScalingEnabled ? ReadDecimal(RawMaxText, "原始上限", errors) : null;
        var engMin = IsScalingEnabled ? ReadDecimal(EngMinText, "工程下限", errors) : null;
        var engMax = IsScalingEnabled ? ReadDecimal(EngMaxText, "工程上限", errors) : null;
        if (IsScalingEnabled
            && (rawMin is null || rawMax is null || engMin is null || engMax is null))
            errors.Add("启用量程换算时必须完整填写四个量程值");
        if (rawMin > rawMax || engMin > engMax)
            errors.Add("量程下限不能大于上限");
        if (errors.Count > 0)
        {
            ValidationMessage = string.Join("；", errors);
            result = null!;
            return false;
        }

        var dataType = SelectedDataTypeOption?.Value ?? DataType;
        var protocol = ProtocolFromDriver(_simplifiedContext.Device.DriverKey);
        var pointId = string.IsNullOrWhiteSpace(_pointId)
            ? Guid.NewGuid().ToString("D")
            : _pointId.Trim();
        var addressText = IsModbus
            ? IsXinjeFieldV1 && XinjeFieldV1AddressParser.TryParse(Address, out var xinjeAddress, out _)
                ? xinjeAddress.Canonical
                : modbusAddress.Canonical
            : Address.Trim();
        var addressDefinition = IsModbus
            ? new PointAddressDefinition
            {
                Area = modbusAddress.Area.ToCode(),
                Offset = modbusAddress.Offset
            }
            : string.Equals(Address.Trim(), _preservedAddress.Trim(), StringComparison.OrdinalIgnoreCase)
                ? _preservedAddressDefinition
                : null;
        // 本产品没有独立的工程数据类型：用户确认保存后，显示类型和驱动原始类型必须一致。
        var code = string.IsNullOrWhiteSpace(Code)
            ? "PT_" + pointId.Replace("-", string.Empty, StringComparison.Ordinal)
            : Code.Trim();
        var groupId = group?.Id;
        if (string.IsNullOrWhiteSpace(groupId))
            groupId = _groupLocked && !string.IsNullOrWhiteSpace(_pendingGroupId)
                ? _pendingGroupId.Trim()
                : "DEFAULT";
        ValidationMessage = string.Empty;
        var riskLevel = writable ? SelectedRiskLevel : WriteRiskLevel.Normal;
        result = new DevicePointDialogResult(
            code,
            Name.Trim(),
            protocol,
            addressText,
            dataType,
            rawMin,
            rawMax,
            engMin,
            engMax,
            writable,
            riskLevel,
            Description.Trim(),
            pointId,
            _simplifiedContext.Device.Id,
            _simplifiedContext.Device.Code,
            groupId,
            addressDefinition,
            s7DecodeOptions,
            _preservedWritePolicy);
        return true;
    }

    private void RefreshGroupOptions(string? preferredGroupId)
    {
        GroupOptions.Clear();
        var deviceId = SelectedDeviceOption?.Id ?? _deviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            SelectedGroupOption = null;
            return;
        }

        var options = GroupEntries
            .Where(group => string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DevicePointGroupChoice(group.Id, group.DeviceId, group.Code, group.Name, group.SortOrder))
            .ToList();
        foreach (var option in options) GroupOptions.Add(option);
        SelectedGroupOption = options.FirstOrDefault(option =>
                string.Equals(option.Id, preferredGroupId, StringComparison.OrdinalIgnoreCase))
            ?? (!IsEdit ? options.FirstOrDefault() : null);
        OnPropertyChanged(nameof(GroupHelpText));
        OnPropertyChanged(nameof(ShowGroupSelection));
        OnPropertyChanged(nameof(ShowCurrentGroup));
        OnPropertyChanged(nameof(CanSave));
    }

    private static DevicePointProtocol ProtocolFromDriver(string? driverKey)
        => DevicePointTypeCatalog.TryParseDriverKey(driverKey, out var protocol)
            ? protocol
            : DevicePointProtocol.Unknown;

    /// <summary>
    /// 把当前点位的访问属性映射为一个不矛盾的客户选项。
    /// </summary>
    private DevicePointWritePolicy FindWritePolicy(bool isWritable, WriteRiskLevel riskLevel)
    {
        _ = riskLevel;
        if (!isWritable) return WritePolicyOptions[0];
        return WritePolicyOptions[1];
    }

    /// <summary>
    /// 解析量程数字，兼容不同小数点写法。
    /// </summary>
    private static decimal? ReadDecimal(string value, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)) return invariant;
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var current)) return current;
        errors.Add($"{label}不是有效数字：{value}");
        return null;
    }

    /// <summary>
    /// 把量程值转换为输入框文字。
    /// </summary>
    private static string Format(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
