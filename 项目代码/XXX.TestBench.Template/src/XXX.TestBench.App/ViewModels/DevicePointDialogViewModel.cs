using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

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
    string Unit,
    decimal? RawMin,
    decimal? RawMax,
    decimal? EngMin,
    decimal? EngMax,
    bool IsWritable,
    WriteRiskLevel RiskLevel,
    bool IsEnabled,
    string Description,
    string PointId = "",
    string DeviceId = "",
    string DeviceCode = "",
    string GroupId = "",
    PointAddressDefinition? AddressDefinition = null,
    string RawDataType = "",
    DecodeOptions? DecodeOptions = null,
    PointWritePolicy WritePolicy = PointWritePolicy.ReadBackEqual)
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
        RawDataType = string.IsNullOrWhiteSpace(RawDataType)
            ? DevicePointTypeCatalog.ToStorage(DataType)
            : RawDataType.Trim(),
        DecodeOptions = DecodeOptions ?? new DecodeOptions(),
        WritePolicy = WritePolicy,
        Unit = Unit.Trim(),
        RawMin = RawMin,
        RawMax = RawMax,
        EngMin = EngMin,
        EngMax = EngMax,
        IsWritable = IsWritable,
        RiskLevel = RiskLevel,
        IsEnabled = IsEnabled,
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
    bool IsEnabled)
{
    public string DisplayName => $"{Code}  {Name}";
    public string StatusText => IsEnabled ? "已启用" : "已停用";
    public string SummaryText => $"{DriverKey} · {StatusText}";
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
    public string DisplayName => $"{Code}  {Name}";
    public string SummaryText => Code == "DEFAULT" ? "默认分组 · 未分组" : "配置分组 · 仅用于组织和筛选";
    public override string ToString() => DisplayName;
}

/// <summary>
/// 点位编辑器中的通信方式选项，业务层使用枚举而不是字符串。
/// </summary>
public sealed record DevicePointProtocolChoice(
    DevicePointProtocol Value,
    string DisplayName,
    string Description)
{
    public override string ToString() => DisplayName;
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
/// 点位写入权限选项。把“是否可写”和“风险等级”合并展示，避免产生矛盾配置。
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
    /// <summary>
    /// 初始化表单，编辑时载入原有点位内容。
    /// </summary>
    public DevicePointDialogViewModel(
        bool isEdit,
        PointsConfig.PointEntry? current,
        IReadOnlyList<DevicePointDeviceChoice>? devices = null,
        string? defaultDeviceId = null,
        IReadOnlyList<PointsConfig.PointGroupEntry>? groups = null,
        string? defaultGroupId = null)
    {
        IsEdit = isEdit;
        if (devices is not null)
            foreach (var device in devices)
                DeviceOptions.Add(device);
        if (groups is not null)
            foreach (var group in groups.OrderBy(item => item.SortOrder).ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase))
                GroupEntries.Add(group);
        UseDeviceSelection = DeviceOptions.Count > 0;
        DialogTitle = isEdit ? "编辑设备点位" : "新增设备点位";
        DialogSubtitle = isEdit
            ? "修改点位信息后保存；所属设备变化时会重新校验地址和类型"
            : "先选择所属设备，再填写该设备驱动可解释的点位地址";

        if (current is null)
        {
            SelectedDeviceOption = DeviceOptions.FirstOrDefault(device =>
                string.Equals(device.Id, defaultDeviceId, StringComparison.OrdinalIgnoreCase))
                ?? DeviceOptions.FirstOrDefault(device => device.IsEnabled)
                ?? DeviceOptions.FirstOrDefault();
            RefreshGroupOptions(defaultGroupId);
            Protocol = UseDeviceSelection
                ? ProtocolFromDriver(SelectedDeviceOption?.DriverKey)
                : DevicePointProtocol.Simulation;
            DataType = DevicePointDataType.Decimal;
            if (!UseDeviceSelection)
                SelectedProtocolOption = ProtocolOptions.Single(option => option.Value == Protocol);
            SelectedDataTypeOption = DataTypeOptions.Single(option => option.Value == DataType);
            SelectedRiskLevel = WriteRiskLevel.Normal;
            SelectedWritePolicy = WritePolicyOptions[0];
            IsEnabled = true;
            return;
        }

        Code = current.Code;
        Name = current.Name;
        _pointId = current.Id;
        _deviceId = current.DeviceId;
        DeviceCode = current.DeviceCode;
        _pendingGroupId = current.GroupId;
        SelectedDeviceOption = DeviceOptions.FirstOrDefault(device =>
            string.Equals(device.Id, current.DeviceId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(device.Code, current.DeviceCode, StringComparison.OrdinalIgnoreCase));
        RefreshGroupOptions(current.GroupId);
        Protocol = DevicePointTypeCatalog.TryParseProtocol(current.Protocol, out var protocol)
            ? protocol
            : ProtocolFromDriver(SelectedDeviceOption?.DriverKey);
        Address = current.Address;
        DataType = DevicePointTypeCatalog.TryParseDataType(current.DataType, out var dataType)
            ? dataType
            : DevicePointDataType.Unknown;
        Unit = current.Unit;
        RawMinText = Format(current.EffectiveRawMin);
        RawMaxText = Format(current.EffectiveRawMax);
        EngMinText = Format(current.EffectiveEngMin);
        EngMaxText = Format(current.EffectiveEngMax);
        IsWritable = current.IsWritable;
        SelectedRiskLevel = current.RiskLevel;
        IsEnabled = current.IsEnabled;
        Description = current.Description;

        if (!UseDeviceSelection)
        {
            SelectedProtocolOption = FindProtocolChoice(
                ProtocolOptions, Protocol, current.Protocol,
                "该通信方式来自已有配置，请确认对应设备驱动已经接入");
        }
        SelectedDataTypeOption = FindDataTypeChoice(
            DataTypeOptions, DataType, current.DataType,
            "该类型来自已有配置，请由设备工程师确认其含义");
        SelectedWritePolicy = FindWritePolicy(IsWritable, SelectedRiskLevel);
    }

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
    /// v2 是否使用所属设备选择。旧版单设备页面仍保留兼容通信方式选项。
    /// </summary>
    public bool UseDeviceSelection { get; }

    /// <summary>
    /// 当前可选择的设备。
    /// </summary>
    public ObservableCollection<DevicePointDeviceChoice> DeviceOptions { get; } = new();

    private readonly List<PointsConfig.PointGroupEntry> GroupEntries = new();

    /// <summary>
    /// 当前所选设备可用的点位分组，切换设备后会重新生成。
    /// </summary>
    public ObservableCollection<DevicePointGroupChoice> GroupOptions { get; } = new();

    public bool UseGroupSelection => UseDeviceSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(DeviceHelpText))]
    private DevicePointDeviceChoice? _selectedDeviceOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(GroupHelpText))]
    private DevicePointGroupChoice? _selectedGroupOption;

    /// <summary>
    /// 所属设备选择说明。
    /// </summary>
    public string DeviceHelpText => SelectedDeviceOption is null
        ? "点位必须归属于已配置设备；此处不直接创建设备。"
        : $"驱动：{SelectedDeviceOption.DriverKey}；{SelectedDeviceOption.StatusText}。地址和类型将由该驱动最终验证。";

    /// <summary>
    /// 所属分组只用于树状组织和筛选，不参与地址解析或运行时路由。
    /// </summary>
    public string GroupHelpText => SelectedGroupOption is null
        ? "请选择当前设备中已存在的分组；留空只能用于旧版单设备兼容编辑器。"
        : "分组只改变树状界面归类，不会改变点位地址、驱动或实时值。";

    /// <summary>
    /// 可选的通信方式。当前模板实际提供仿真运行时；已有不受支持的配置会以兼容项显示，需改选已支持类型后才能保存。
    /// </summary>
    public ObservableCollection<DevicePointProtocolChoice> ProtocolOptions { get; } =
    [
        new(DevicePointProtocol.Simulation, "仿真", "用于开发、演示和离线联调，不连接现场设备")
    ];

    /// <summary>
    /// 可选的数据类型，显示中文含义并保留驱动使用的内部值。
    /// </summary>
    public ObservableCollection<DevicePointDataTypeChoice> DataTypeOptions { get; } =
    [
        new(DevicePointDataType.Boolean, "开关量", "只有“是/否”或“开/关”两种状态"),
        new(DevicePointDataType.Decimal, "小数", "压力、温度、电流等连续数值"),
        new(DevicePointDataType.Int32, "整数", "不带小数的计数值或状态码"),
        new(DevicePointDataType.Bool, "布尔量", "按位解码的原始布尔值"),
        new(DevicePointDataType.Int16, "16 位有符号整数", "两个字节的有符号整数"),
        new(DevicePointDataType.UInt16, "16 位无符号整数", "两个字节的无符号整数"),
        new(DevicePointDataType.UInt32, "32 位无符号整数", "四个字节的无符号整数"),
        new(DevicePointDataType.Float32, "单精度浮点数", "四个字节的 IEEE 754 单精度浮点数"),
        new(DevicePointDataType.String, "文本", "设备返回或需要写入的一段文字")
    ];

    /// <summary>
    /// 可选的写入权限。高风险含义是“写错可能改变设备动作”，不是简单的技术类型。
    /// </summary>
    public ObservableCollection<DevicePointWritePolicy> WritePolicyOptions { get; } =
    [
        new("ReadOnly", "只读", "只采集和显示数据，不允许人工写入", false, WriteRiskLevel.Normal),
        new("NormalWritable", "可写（普通）", "可在受控流程中写入，需要手动控制权限", true, WriteRiskLevel.Normal),
        new("HighRiskWritable", "可写（高风险）", "可能影响启动、停止、复位或输出，写入时需要更高权限和二次确认", true, WriteRiskLevel.HighRisk)
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private DevicePointProtocolChoice? _selectedProtocolOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private DevicePointDataTypeChoice? _selectedDataTypeOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private DevicePointWritePolicy? _selectedWritePolicy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    /// <summary>
    /// 点位编码输入。
    /// </summary>
    private string _code = string.Empty;

    [ObservableProperty]
    /// <summary>
    /// 点位名称输入。
    /// </summary>
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
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
    /// 单位输入。
    /// </summary>
    private string _unit = string.Empty;

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

    [ObservableProperty]
    /// <summary>
    /// 是否启用该点位。
    /// </summary>
    private bool _isEnabled = true;

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
    /// 当前通信方式的解释，帮助客户理解协议字段和地址的关系。
    /// </summary>
    public string ProtocolHelpText => SelectedProtocolOption?.Description
        ?? "通信方式决定系统如何解释下面的设备地址；它不是点位名称，也不是设备品牌。";

    /// <summary>
    /// 当前数据类型的解释。
    /// </summary>
    public string DataTypeHelpText => SelectedDataTypeOption?.Description
        ?? "系统会按照所选类型解析设备返回值，并校验需要写入的值。";

    /// <summary>
    /// 当前通信方式对应的地址填写提示。
    /// </summary>
    public string AddressHint => Protocol == DevicePointProtocol.Simulation
        ? "仿真地址示例：sim.pressure"
        : "填写该通信方式规定的设备地址，例如 DB1.DBW0 或 40001";

    /// <summary>
    /// 当前写入权限对应的安全提示。
    /// </summary>
    public string WriteSafetyMessage => SelectedWritePolicy?.Description
        ?? "写入权限只声明点位是否允许被受控写入；保存此设置不会立即向设备写入数据。";

    partial void OnSelectedProtocolOptionChanged(DevicePointProtocolChoice? value)
    {
        Protocol = value?.Value ?? DevicePointProtocol.Unknown;
        OnPropertyChanged(nameof(ProtocolHelpText));
        OnPropertyChanged(nameof(AddressHint));
    }

    partial void OnSelectedDeviceOptionChanged(DevicePointDeviceChoice? value)
    {
        if (UseDeviceSelection)
        {
            Protocol = ProtocolFromDriver(value?.DriverKey);
            _deviceId = value?.Id ?? string.Empty;
            DeviceCode = value?.Code ?? string.Empty;
            RefreshGroupOptions(_pendingGroupId);
            _pendingGroupId = string.Empty;
        }
        OnPropertyChanged(nameof(DeviceHelpText));
        OnPropertyChanged(nameof(AddressHint));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnSelectedGroupOptionChanged(DevicePointGroupChoice? value)
    {
        OnPropertyChanged(nameof(GroupHelpText));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnSelectedDataTypeOptionChanged(DevicePointDataTypeChoice? value)
    {
        DataType = value?.Value ?? DevicePointDataType.Unknown;
        OnPropertyChanged(nameof(DataTypeHelpText));
    }

    partial void OnSelectedWritePolicyChanged(DevicePointWritePolicy? value)
    {
        if (value is not null)
        {
            IsWritable = value.IsWritable;
            SelectedRiskLevel = value.RiskLevel;
        }

        OnPropertyChanged(nameof(WriteSafetyMessage));
    }

    private string _validationMessage = string.Empty;
    /// <summary>
    /// 校验失败时展示的提示。
    /// </summary>
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    /// <summary>
    /// 必填项是否齐全，控制保存按钮可用性。
    /// </summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Code)
        && (!UseDeviceSelection || SelectedDeviceOption is not null)
        && (!UseGroupSelection || SelectedGroupOption is not null)
        && (UseDeviceSelection || Protocol != DevicePointProtocol.Unknown)
        && !string.IsNullOrWhiteSpace(Address)
        && DataType != DevicePointDataType.Unknown
        && (UseDeviceSelection || SelectedProtocolOption is not null)
        && SelectedDataTypeOption is not null
        && SelectedWritePolicy is not null;

    /// <summary>
    /// 校验输入并生成提交结果，失败时给出提示。
    /// </summary>
    public bool TryBuildResult(out DevicePointDialogResult result)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code)) errors.Add("点位编码不能为空");
        if (UseDeviceSelection)
        {
            if (SelectedDeviceOption is null) errors.Add("请选择所属设备");
            else if (!SelectedDeviceOption.IsEnabled) errors.Add("所属设备已停用，不能新增或应用点位");
            if (SelectedGroupOption is null) errors.Add("请选择所属分组");
        }
        else if (SelectedProtocolOption is null || Protocol == DevicePointProtocol.Unknown)
            errors.Add("请选择通信方式");
        if (string.IsNullOrWhiteSpace(Address)) errors.Add("地址不能为空");
        if (SelectedDataTypeOption is null || DataType == DevicePointDataType.Unknown) errors.Add("请选择数据类型");
        if (SelectedWritePolicy is null) errors.Add("请选择写入权限");

        var rawMin = ReadDecimal(RawMinText, "原始下限", errors);
        var rawMax = ReadDecimal(RawMaxText, "原始上限", errors);
        var engMin = ReadDecimal(EngMinText, "工程下限", errors);
        var engMax = ReadDecimal(EngMaxText, "工程上限", errors);
        if (rawMin.HasValue != rawMax.HasValue || engMin.HasValue != engMax.HasValue || rawMin.HasValue != engMin.HasValue)
            errors.Add("量程必须完整填写原始/工程上下限，或全部留空");
        if (rawMin > rawMax || engMin > engMax)
            errors.Add("量程下限不能大于上限");

        if (errors.Count > 0)
        {
            ValidationMessage = string.Join("；", errors);
            result = null!;
            return false;
        }

        ValidationMessage = string.Empty;
        var protocol = UseDeviceSelection
            ? ProtocolFromDriver(SelectedDeviceOption?.DriverKey)
            : SelectedProtocolOption?.Value ?? Protocol;
        var dataType = SelectedDataTypeOption?.Value ?? DataType;
        var isWritable = SelectedWritePolicy?.IsWritable ?? IsWritable;
        var riskLevel = SelectedWritePolicy?.RiskLevel ?? SelectedRiskLevel;
        result = new DevicePointDialogResult(
            Code.Trim(), Name.Trim(), protocol, Address.Trim(), dataType, Unit.Trim(),
            rawMin, rawMax, engMin, engMax, isWritable, riskLevel, IsEnabled, Description.Trim(),
            _pointId, SelectedDeviceOption?.Id ?? _deviceId, SelectedDeviceOption?.Code ?? DeviceCode,
            SelectedGroupOption?.Id ?? string.Empty,
            null, DevicePointTypeCatalog.ToStorage(dataType), new DecodeOptions(), PointWritePolicy.ReadBackEqual);
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
            .Where(group => string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DevicePointGroupChoice(group.Id, group.DeviceId, group.Code, group.Name, group.SortOrder))
            .ToList();
        foreach (var option in options) GroupOptions.Add(option);
        SelectedGroupOption = options.FirstOrDefault(option =>
                string.Equals(option.Id, preferredGroupId, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault(option => string.Equals(option.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault();
        OnPropertyChanged(nameof(GroupHelpText));
        OnPropertyChanged(nameof(CanSave));
    }

    private static DevicePointProtocol ProtocolFromDriver(string? driverKey)
        => DevicePointTypeCatalog.TryParseDriverKey(driverKey, out var protocol)
            ? protocol
            : DevicePointProtocol.Unknown;

    /// <summary>
    /// 在不破坏已有配置的前提下，把旧值放入下拉选项中。
    /// </summary>
    private static DevicePointProtocolChoice? FindProtocolChoice(
        ObservableCollection<DevicePointProtocolChoice> options,
        DevicePointProtocol value,
        string rawValue,
        string description)
    {
        if (value != DevicePointProtocol.Unknown)
        {
            var existing = options.FirstOrDefault(item => item.Value == value);
            if (existing is not null) return existing;
        }

        if (string.IsNullOrWhiteSpace(rawValue)) return null;
        var compatibility = new DevicePointProtocolChoice(
            DevicePointProtocol.Unknown,
            "当前配置：" + rawValue.Trim(),
            description);
        options.Add(compatibility);
        return compatibility;
    }

    private static DevicePointDataTypeChoice? FindDataTypeChoice(
        ObservableCollection<DevicePointDataTypeChoice> options,
        DevicePointDataType value,
        string rawValue,
        string description)
    {
        if (value != DevicePointDataType.Unknown)
        {
            var existing = options.FirstOrDefault(item => item.Value == value);
            if (existing is not null) return existing;
        }

        if (string.IsNullOrWhiteSpace(rawValue)) return null;
        var compatibility = new DevicePointDataTypeChoice(
            DevicePointDataType.Unknown,
            "当前配置：" + rawValue.Trim(),
            description);
        options.Add(compatibility);
        return compatibility;
    }

    /// <summary>
    /// 把旧配置中的写入属性映射为一个不矛盾的客户选项。
    /// </summary>
    private DevicePointWritePolicy FindWritePolicy(bool isWritable, WriteRiskLevel riskLevel)
    {
        if (!isWritable) return WritePolicyOptions[0];
        return riskLevel == WriteRiskLevel.HighRisk ? WritePolicyOptions[2] : WritePolicyOptions[1];
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
