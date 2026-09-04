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
    string Protocol,
    string Address,
    string DataType,
    string Unit,
    decimal? RawMin,
    decimal? RawMax,
    decimal? EngMin,
    decimal? EngMax,
    bool IsWritable,
    WriteRiskLevel RiskLevel,
    bool IsEnabled,
    string Description)
{
    /// <summary>
    /// 把弹窗结果转换为可保存的点位配置。
    /// </summary>
    public PointsConfig.PointEntry ToEntry() => new()
    {
        Code = Code.Trim(),
        Name = string.IsNullOrWhiteSpace(Name) ? Code.Trim() : Name.Trim(),
        Protocol = Protocol.Trim(),
        Address = Address.Trim(),
        DataType = DataType.Trim(),
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
/// 设备点位新增/编辑表单。必填字段和量程关系在提交时再次校验。
/// </summary>
public sealed partial class DevicePointDialogViewModel : ObservableObject
{
    /// <summary>
    /// 初始化表单，编辑时载入原有点位内容。
    /// </summary>
    public DevicePointDialogViewModel(bool isEdit, PointsConfig.PointEntry? current)
    {
        IsEdit = isEdit;
        DialogTitle = isEdit ? "编辑设备点位" : "新增设备点位";
        DialogSubtitle = isEdit
            ? "修改点位元数据后保存，运行时将重新加载点位目录"
            : "填写协议地址与数据类型，建立运行时可读取的点位来源";

        if (current is null)
        {
            SelectedRiskLevel = WriteRiskLevel.Normal;
            IsEnabled = true;
            return;
        }

        Code = current.Code;
        Name = current.Name;
        Protocol = current.Protocol;
        Address = current.Address;
        DataType = current.DataType;
        Unit = current.Unit;
        RawMinText = Format(current.EffectiveRawMin);
        RawMaxText = Format(current.EffectiveRawMax);
        EngMinText = Format(current.EffectiveEngMin);
        EngMaxText = Format(current.EffectiveEngMax);
        IsWritable = current.IsWritable;
        SelectedRiskLevel = current.RiskLevel;
        IsEnabled = current.IsEnabled;
        Description = current.Description;
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
    /// 可选的写入风险等级。
    /// </summary>
    public ObservableCollection<WriteRiskLevel> RiskOptions { get; } = new(Enum.GetValues<WriteRiskLevel>());

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
    private string _protocol = string.Empty;

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
    private string _dataType = string.Empty;

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

    [ObservableProperty]
    /// <summary>
    /// 点位说明输入。
    /// </summary>
    private string _description = string.Empty;

    private string _validationMessage = string.Empty;
    /// <summary>
    /// 校验失败时展示的提示。
    /// </summary>
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    /// <summary>
    /// 必填项是否齐全，控制保存按钮可用性。
    /// </summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Code)
        && !string.IsNullOrWhiteSpace(Protocol)
        && !string.IsNullOrWhiteSpace(Address)
        && !string.IsNullOrWhiteSpace(DataType);

    /// <summary>
    /// 校验输入并生成提交结果，失败时给出提示。
    /// </summary>
    public bool TryBuildResult(out DevicePointDialogResult result)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code)) errors.Add("点位编码不能为空");
        if (string.IsNullOrWhiteSpace(Protocol)) errors.Add("协议不能为空");
        if (string.IsNullOrWhiteSpace(Address)) errors.Add("地址不能为空");
        if (string.IsNullOrWhiteSpace(DataType)) errors.Add("数据类型不能为空");

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
        result = new DevicePointDialogResult(
            Code.Trim(), Name.Trim(), Protocol.Trim(), Address.Trim(), DataType.Trim(), Unit.Trim(),
            rawMin, rawMax, engMin, engMax, IsWritable, SelectedRiskLevel, IsEnabled, Description.Trim());
        return true;
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
