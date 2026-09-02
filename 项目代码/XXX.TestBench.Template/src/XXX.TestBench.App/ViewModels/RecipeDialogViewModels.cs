using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.TestDefinitions;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 工艺界面型号选择表格的一行。
/// </summary>
public sealed record ProductModelSelectionOption(
    int ProductTypeId,
    string ProductTypeCode,
    string ProductTypeName,
    int ProductModelId,
    string ProductModelCode,
    string ProductModelName,
    bool IsEnabled)
{
    public string TypeDisplay => $"{ProductTypeCode}  {ProductTypeName}";
    public string ModelDisplay => $"{ProductModelCode}  {ProductModelName}";
    public string StatusText => IsEnabled ? "启用" : "停用";
}

/// <summary>
/// 型号选择弹窗状态：展示可选型号并跟踪选中项。
/// </summary>
public sealed partial class ProductModelSelectionViewModel : ObservableObject
{
    public ProductModelSelectionViewModel(IEnumerable<ProductModelSelectionOption> options)
    {
        foreach (var option in options)
            Options.Add(option);
    }

    /// <summary>
    /// 弹窗展示的可选型号列表。
    /// </summary>
    public ObservableCollection<ProductModelSelectionOption> Options { get; } = new();

    /// <summary>
    /// 是否存在可选型号。
    /// </summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// 可选型号数量。
    /// </summary>
    public int OptionCount => Options.Count;

    /// <summary>
    /// 当前选中的型号；变化时自动刷新“可否确认”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private ProductModelSelectionOption? _selectedOption;

    /// <summary>
    /// 是否已选中型号，决定确认按钮是否可用。
    /// </summary>
    public bool CanConfirm => SelectedOption is not null;
}

/// <summary>
/// 新增产品类型或产品型号弹窗的提交结果。
/// </summary>
public sealed record ProductMasterDataDialogResult(string Code, string Name, int? ProductTypeId);

/// <summary>
/// 产品类型与产品型号共用的弹窗表单状态。
/// </summary>
public sealed partial class ProductMasterDataDialogViewModel : ObservableObject
{
    public ProductMasterDataDialogViewModel(bool isModelDialog, IEnumerable<ProductType> typeOptions)
    {
        IsModelDialog = isModelDialog;
        foreach (var type in typeOptions)
            TypeOptions.Add(type);
    }

    /// <summary>
    /// 当前弹窗是否用于新增产品型号，否则用于新增产品类型。
    /// </summary>
    public bool IsModelDialog { get; }

    /// <summary>
    /// 当前弹窗是否用于新增产品类型。
    /// </summary>
    public bool IsTypeDialog => !IsModelDialog;

    /// <summary>
    /// 弹窗标题文字。
    /// </summary>
    public string DialogTitle => IsModelDialog ? "新增产品型号" : "新增产品类型";

    /// <summary>
    /// 弹窗副标题说明文字。
    /// </summary>
    public string DialogSubtitle => IsModelDialog
        ? "选择所属产品类型，填写型号代码和名称"
        : "填写产品类型代码和名称，保存后将在列表中显示";

    /// <summary>
    /// 弹窗展示的产品类型选项列表。
    /// </summary>
    public ObservableCollection<ProductType> TypeOptions { get; } = new();

    /// <summary>
    /// 新增产品型号时选中的所属产品类型；变化时刷新“可否保存”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private ProductType? _selectedType;

    /// <summary>
    /// 录入的代码；变化时刷新“可否保存”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _code = string.Empty;

    /// <summary>
    /// 录入的名称。
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    private string _validationMessage = string.Empty;

    /// <summary>
    /// 校验失败时展示的提示文字。
    /// </summary>
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    /// <summary>
    /// 是否满足保存条件：代码非空，且类型弹窗或已选型号。
    /// </summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Code) && (IsTypeDialog || SelectedType is not null);

    /// <summary>
    /// 校验表单并尝试生成提交结果。
    /// </summary>
    public bool TryBuildResult(out ProductMasterDataDialogResult result)
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            ValidationMessage = "请输入代码";
            result = null!;
            return false;
        }

        if (IsModelDialog && SelectedType is null)
        {
            ValidationMessage = "请选择所属产品类型";
            result = null!;
            return false;
        }

        ValidationMessage = string.Empty;
        result = new ProductMasterDataDialogResult(Code.Trim(), Name.Trim(), SelectedType?.Id);
        return true;
    }
}

/// <summary>
/// 新增试验项弹窗的提交结果。
/// </summary>
public sealed record TestItemDefinitionDialogResult(string Code, string Name, string ExecutorCode);

/// <summary>
/// 新增试验项弹窗状态。
/// </summary>
public sealed partial class TestItemDefinitionDialogViewModel : ObservableObject
{
    /// <summary>
    /// 录入的试验项代码；变化时刷新“可否保存”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _code = string.Empty;

    /// <summary>
    /// 录入的试验项名称。
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 录入的执行器代码；变化时刷新“可否保存”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _executorCode = string.Empty;

    private string _validationMessage = string.Empty;

    /// <summary>
    /// 校验失败时展示的提示文字。
    /// </summary>
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    /// <summary>
    /// 是否满足保存条件：代码与执行器代码都非空。
    /// </summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Code) && !string.IsNullOrWhiteSpace(ExecutorCode);

    /// <summary>
    /// 校验表单并尝试生成提交结果。
    /// </summary>
    public bool TryBuildResult(out TestItemDefinitionDialogResult result)
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            ValidationMessage = "请输入试验项代码";
            result = null!;
            return false;
        }

        if (string.IsNullOrWhiteSpace(ExecutorCode))
        {
            ValidationMessage = "请输入执行器代码";
            result = null!;
            return false;
        }

        ValidationMessage = string.Empty;
        result = new TestItemDefinitionDialogResult(Code.Trim(), Name.Trim(), ExecutorCode.Trim());
        return true;
    }
}

/// <summary>
/// 参数数据类型的可读选项。
/// </summary>
public sealed record ParameterDataTypeOption(ParameterDataType Value, string Name);

/// <summary>
/// 新增参数弹窗的提交结果。
/// </summary>
public sealed record ParameterDefinitionDialogResult(
    int TestItemDefinitionId,
    string Code,
    string Name,
    ParameterDataType DataType,
    bool IsRequired,
    string? Unit,
    decimal? MinValue,
    decimal? MaxValue,
    int? Precision,
    IReadOnlyList<string> AllowedValues);

/// <summary>
/// 新增参数弹窗状态与边界校验。
/// </summary>
public sealed partial class ParameterDefinitionDialogViewModel : ObservableObject
{
    public ParameterDefinitionDialogViewModel(IEnumerable<TestItemDefinition> itemOptions)
    {
        foreach (var item in itemOptions)
            ItemOptions.Add(item);
        _selectedDataType = DataTypeOptions.First(option => option.Value == ParameterDataType.Decimal);
    }

    /// <summary>
    /// 弹窗展示的试验项选项列表。
    /// </summary>
    public ObservableCollection<TestItemDefinition> ItemOptions { get; } = new();

    /// <summary>
    /// 弹窗展示的参数数据类型选项。
    /// </summary>
    public IReadOnlyList<ParameterDataTypeOption> DataTypeOptions { get; } =
    [
        new(ParameterDataType.Boolean, "布尔"),
        new(ParameterDataType.Integer, "整数"),
        new(ParameterDataType.Decimal, "小数"),
        new(ParameterDataType.Text, "文本"),
        new(ParameterDataType.Enum, "枚举")
    ];

    /// <summary>
    /// 选中的所属试验项；变化时刷新“可否保存”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private TestItemDefinition? _selectedItem;

    /// <summary>
    /// 选中的数据类型；变化时刷新枚举提示与“可否保存”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnum))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private ParameterDataTypeOption? _selectedDataType;

    /// <summary>
    /// 当前是否为枚举类型参数。
    /// </summary>
    public bool IsEnum => SelectedDataType?.Value == ParameterDataType.Enum;

    /// <summary>
    /// 录入的参数代码；变化时刷新“可否保存”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _code = string.Empty;

    /// <summary>
    /// 录入的参数名称。
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 参数是否必填。
    /// </summary>
    [ObservableProperty]
    private bool _isRequired;

    /// <summary>
    /// 参数单位。
    /// </summary>
    [ObservableProperty]
    private string _unit = string.Empty;

    /// <summary>
    /// 参数下限文字。
    /// </summary>
    [ObservableProperty]
    private string _minValue = string.Empty;

    /// <summary>
    /// 参数上限文字。
    /// </summary>
    [ObservableProperty]
    private string _maxValue = string.Empty;

    /// <summary>
    /// 参数小数精度文字。
    /// </summary>
    [ObservableProperty]
    private string _precision = string.Empty;

    /// <summary>
    /// 枚举参数的候选值文字，用逗号或分号分隔。
    /// </summary>
    [ObservableProperty]
    private string _allowedValues = string.Empty;

    private string _validationMessage = string.Empty;

    /// <summary>
    /// 校验失败时展示的提示文字。
    /// </summary>
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    /// <summary>
    /// 是否满足保存条件：已选试验项且代码非空。
    /// </summary>
    public bool CanSave => SelectedItem is not null && !string.IsNullOrWhiteSpace(Code);

    /// <summary>
    /// 校验表单并尝试生成提交结果。
    /// </summary>
    public bool TryBuildResult(out ParameterDefinitionDialogResult result)
    {
        if (SelectedItem is null)
        {
            ValidationMessage = "请选择所属试验项";
            result = null!;
            return false;
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            ValidationMessage = "请输入参数代码";
            result = null!;
            return false;
        }

        if (!TryParseOptionalDecimal(MinValue, out var min) || !TryParseOptionalDecimal(MaxValue, out var max))
        {
            ValidationMessage = "参数上下限必须是有效数值";
            result = null!;
            return false;
        }

        if (min.HasValue && max.HasValue && min > max)
        {
            ValidationMessage = "参数下限不能大于上限";
            result = null!;
            return false;
        }

        int? precision = null;
        if (!string.IsNullOrWhiteSpace(Precision))
        {
            if (!int.TryParse(Precision.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPrecision) || parsedPrecision < 0)
            {
                ValidationMessage = "精度必须是非负整数";
                result = null!;
                return false;
            }
            precision = parsedPrecision;
        }

        var allowedValues = AllowedValues
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (SelectedDataType?.Value == ParameterDataType.Enum && allowedValues.Length == 0)
        {
            ValidationMessage = "枚举参数至少需要一个可选值";
            result = null!;
            return false;
        }

        ValidationMessage = string.Empty;
        result = new ParameterDefinitionDialogResult(
            SelectedItem.Id,
            Code.Trim(),
            Name.Trim(),
            SelectedDataType?.Value ?? ParameterDataType.Decimal,
            IsRequired,
            string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim(),
            min,
            max,
            precision,
            allowedValues);
        return true;
    }

    /// <summary>
    /// 尝试把输入文字解析成可选的小数，空文字视为通过。
    /// </summary>
    private static bool TryParseOptionalDecimal(string text, out decimal? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var currentValue)
            || decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out currentValue))
        {
            value = currentValue;
            return true;
        }

        value = null;
        return false;
    }
}