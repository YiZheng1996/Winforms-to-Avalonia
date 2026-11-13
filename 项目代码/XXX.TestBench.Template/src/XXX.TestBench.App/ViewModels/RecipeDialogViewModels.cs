using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Domain.Products;


namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 工艺界面型号选择表格的一行。
/// </summary>
public sealed record ProductModelSelectionOption(
    int ProductTypeId,
    string ProductTypeName,
    int ProductModelId,
    string ProductModelName,
    bool IsEnabled,
    DateTime CreatedAtUtc)
{
    /// <summary>
    /// 产品类型的用户可见名称；编号仅用于内部关联。
    /// </summary>
    public string TypeDisplay => ProductTypeName;

    /// <summary>
    /// 产品型号的用户可见名称；编号仅用于内部关联。
    /// </summary>
    public string ModelDisplay => ProductModelName;
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
/// 新增或编辑产品类型、产品型号弹窗的提交结果；关联关系使用数据库 ID。
/// </summary>
public sealed record ProductMasterDataDialogResult(string Name, int? ProductTypeId);

/// <summary>
/// 产品类型与产品型号共用的弹窗表单状态。
/// </summary>
public sealed partial class ProductMasterDataDialogViewModel : ObservableObject
{
    public ProductMasterDataDialogViewModel(
        bool isModelDialog,
        IEnumerable<ProductType> typeOptions,
        bool isEdit = false,
        string name = "",
        int? typeId = null)
    {
        IsModelDialog = isModelDialog;
        IsEdit = isEdit;
        foreach (var type in typeOptions)
            TypeOptions.Add(type);

        Name = name;
        if (IsModelDialog && typeId is not null)
            SelectedType = TypeOptions.FirstOrDefault(type => type.Id == typeId.Value);
    }

    /// <summary>
    /// 当前弹窗是否用于产品型号（否则用于产品类型），新增与编辑共用。
    /// </summary>
    public bool IsModelDialog { get; }

    /// <summary>
    /// 当前弹窗是否用于产品类型（否则用于产品型号）。
    /// </summary>
    public bool IsTypeDialog => !IsModelDialog;

    /// <summary>
    /// 当前弹窗是否用于编辑已有记录。
    /// </summary>
    public bool IsEdit { get; }

    /// <summary>
    /// 编辑产品型号时是否允许修改所属产品类型；为 false 表示型号编辑时锁定原类型。
    /// </summary>
    public bool CanSelectType => !(IsModelDialog && IsEdit);

    /// <summary>
    /// 弹窗标题文字。
    /// </summary>
    public string DialogTitle => (IsModelDialog, IsEdit) switch
    {
        (false, false) => "新增产品类型",
        (false, true) => "编辑产品类型",
        (true, false) => "新增产品型号",
        _ => "编辑产品型号"
    };

    /// <summary>
    /// 弹窗副标题说明文字。
    /// </summary>
    public string DialogSubtitle => IsEdit
        ? (IsModelDialog ? "修改型号名称，所属产品类型保持不变" : "修改产品类型名称")
        : (IsModelDialog ? "选择所属产品类型，填写型号名称" : "填写产品类型名称，保存后将自动生成编号");

    /// <summary>
    /// 弹窗展示的产品类型选项列表。
    /// </summary>
    public ObservableCollection<ProductType> TypeOptions { get; } = new();

    /// <summary>
    ///可否保存。“可否保存”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private ProductType? _selectedType;

    /// <summary>
    /// 录入的名称；变化时刷新“可否保存”。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name = string.Empty;

    private string _validationMessage = string.Empty;

    /// <summary>
    /// 校验失败时展示的提示文字。
    /// </summary>
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    /// <summary>
    /// 是否满足保存条件：名称非空，且类型弹窗或已选所属类型。
    /// </summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Name) && (IsTypeDialog || SelectedType is not null);

    /// <summary>
    /// 校验表单并尝试生成提交结果。
    /// </summary>
    public bool TryBuildResult(out ProductMasterDataDialogResult result)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = "请输入名称";
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
        result = new ProductMasterDataDialogResult(Name.Trim(), SelectedType?.Id);
        return true;
    }
}
