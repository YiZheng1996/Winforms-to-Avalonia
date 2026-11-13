using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 页面操作反馈，供页面在统一弹窗中展示成功或失败结果。
/// </summary>
public sealed record OperationFeedback(bool Succeeded, string Message)
{
    public static OperationFeedback Success(string message) => new(true, message);

    public static OperationFeedback Failure(string message) => new(false, message);
}

/// <summary>
/// 通用操作结果弹窗模型。
/// </summary>
public sealed class NoticeDialogViewModel
{
    public NoticeDialogViewModel(string title, string message, bool isError)
    {
        Title = title;
        Message = message;
        IsError = isError;
    }

    public string Title { get; }
    public string Message { get; }
    public bool IsError { get; }
}

/// <summary>
/// 固定试验参数编辑弹窗模型。
/// </summary>
public sealed partial class ParameterValueDialogViewModel : ObservableObject
{
    public ParameterValueDialogViewModel(
        string dialogTitle,
        string dialogSubtitle,
        string fieldLabel,
        string unit,
        string watermark,
        string value,
        bool integerOnly)
    {
        DialogTitle = dialogTitle;
        DialogSubtitle = dialogSubtitle;
        FieldLabel = fieldLabel;
        Unit = unit;
        Watermark = watermark;
        IntegerOnly = integerOnly;
        ValueInput = value;
    }

    public string DialogTitle { get; }
    public string DialogSubtitle { get; }
    public string FieldLabel { get; }
    public string Unit { get; }
    public string Watermark { get; }
    public bool IntegerOnly { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _valueInput = string.Empty;

    private string _validationMessage = string.Empty;

    /// <summary>
    /// 校验失败时在弹窗内展示的具体原因。
    /// </summary>
    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(ValueInput);

    public bool TryBuildResult(out string value)
    {
        value = ValueInput.Trim();
        if (value.Length == 0)
        {
            ValidationMessage = "请输入参数值";
            return false;
        }

        var isValid = IntegerOnly
            ? int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        if (!isValid)
        {
            ValidationMessage = IntegerOnly ? "参数值必须为整数" : "参数值必须为数值";
            return false;
        }

        ValidationMessage = string.Empty;
        return true;
    }
}

/// <summary>
/// 产品组合级试验参数编辑弹窗模型。
/// </summary>
public sealed partial class ProductParameterDialogViewModel : ObservableObject
{
    public ProductParameterDialogViewModel(
        string dialogTitle,
        string dialogSubtitle,
        string testVoltageInput,
        string protectCurrentInput)
    {
        DialogTitle = dialogTitle;
        DialogSubtitle = dialogSubtitle;
        TestVoltageInput = testVoltageInput;
        ProtectCurrentInput = protectCurrentInput;
    }

    public string DialogTitle { get; }
    public string DialogSubtitle { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _testVoltageInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _protectCurrentInput = string.Empty;

    private string _validationMessage = string.Empty;

    /// <summary>
    /// 校验失败时在弹窗内展示的具体原因。
    /// </summary>
    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool CanSave
        => !string.IsNullOrWhiteSpace(TestVoltageInput)
        && !string.IsNullOrWhiteSpace(ProtectCurrentInput);

    public bool TryBuildResult(out ProductParameterDialogResult result)
    {
        var voltage = TestVoltageInput.Trim();
        var current = ProtectCurrentInput.Trim();
        result = new ProductParameterDialogResult(voltage, current);

        if (voltage.Length == 0 || current.Length == 0)
        {
            ValidationMessage = "请完整填写试验电压和保护电流";
            return false;
        }

        if (!double.TryParse(voltage, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            ValidationMessage = "试验电压必须为数值";
            return false;
        }

        if (!double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            ValidationMessage = "保护电流必须为数值";
            return false;
        }

        ValidationMessage = string.Empty;
        return true;
    }
}

/// <summary>
/// 产品组合级试验参数编辑结果。
/// </summary>
public sealed record ProductParameterDialogResult(string TestVoltage, string ProtectCurrent);
