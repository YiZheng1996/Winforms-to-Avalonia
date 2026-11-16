using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 试验项点编辑弹窗的提交结果；项点 ID 由数据库生成。
/// </summary>
public sealed record TestPointDialogResult(string Name, string ExecutorCode, string ResultKind, int SortOrder, bool IsEnabled);

/// <summary>
/// 试验项点新增/编辑弹窗表单状态：项点名称 + 关联逻辑类（编译期注册执行器）+ 排序 + 启用。
/// </summary>
public sealed partial class TestPointDialogViewModel : ObservableObject
{
    public TestPointDialogViewModel(bool isEdit, int? pointId, string name, string executorCode, int sortOrder, bool isEnabled, IEnumerable<string> executorCodes)
    {
        IsEdit = isEdit;
        PointId = pointId;
        Name = name;
        SelectedExecutorCode = executorCode;
        SortOrderText = sortOrder.ToString(CultureInfo.InvariantCulture);
        IsEnabled = isEnabled;
        foreach (var executor in executorCodes)
            ExecutorOptions.Add(executor);
    }

    /// <summary>
    /// 是否编辑已有项点（否则为新增）。
    /// </summary>
    public bool IsEdit { get; }

    /// <summary>
    /// 编辑时的项点 ID，新增为 null。
    /// </summary>
    public int? PointId { get; }

    /// <summary>
    /// 项点名称。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name = string.Empty;

    /// <summary>
    /// 已注册的关联逻辑类（执行器代码）选项。
    /// </summary>
    public ObservableCollection<string> ExecutorOptions { get; } = new();

    /// <summary>
    /// 选中的关联逻辑类。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string? _selectedExecutorCode;

    /// <summary>
    /// 排序号输入文字。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _sortOrderText = "1";

    /// <summary>
    /// 是否启用。
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    private string _validationMessage = string.Empty;

    /// <summary>
    /// 校验失败时展示的提示文字。
    /// </summary>
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    /// <summary>
    /// 标题文字。
    /// </summary>
    public string DialogTitle => IsEdit ? "编辑试验项点" : "新增试验项点";

    /// <summary>
    /// 副标题文字。
    /// </summary>
    public string DialogSubtitle => IsEdit
        ? "修改项点名称、关联逻辑类、排序与启用状态"
        : "填写项点名称，并选择编译期已注册的关联逻辑类";

    /// <summary>
    /// 是否满足保存条件。
    /// </summary>
    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Name) &&
        SelectedExecutorCode is not null &&
        int.TryParse(SortOrderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// 校验表单并尝试生成提交结果。
    /// </summary>
    public bool TryBuildResult(out TestPointDialogResult result)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = "请输入项点名称";
            result = null!;
            return false;
        }
        if (SelectedExecutorCode is null)
        {
            ValidationMessage = "请选择关联逻辑类";
            result = null!;
            return false;
        }
        if (!int.TryParse(SortOrderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sortOrder) || sortOrder < 0)
        {
            ValidationMessage = "排序号必须为不小于 0 的整数";
            result = null!;
            return false;
        }

        ValidationMessage = string.Empty;
        result = new TestPointDialogResult(Name.Trim(), SelectedExecutorCode, "PassFail", sortOrder, IsEnabled);
        return true;
    }
}
