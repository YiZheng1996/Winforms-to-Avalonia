using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 点位分组弹窗提交结果。
/// </summary>
public sealed record PointGroupDialogResult(
    string GroupId,
    string DeviceId,
    string Code,
    string Name,
    int SortOrder,
    string Description)
{
    public PointsConfig.PointGroupEntry ToEntry() => new()
    {
        Id = string.IsNullOrWhiteSpace(GroupId)
            ? Guid.NewGuid().ToString("D")
            : GroupId.Trim(),
        DeviceId = DeviceId.Trim(),
        Code = Code.Trim(),
        Name = string.IsNullOrWhiteSpace(Name) ? Code.Trim() : Name.Trim(),
        SortOrder = SortOrder,
        Description = Description.Trim()
    };
}

/// <summary>
/// 单层点位分组编辑器。分组不包含地址、驱动或实时值。
/// </summary>
public sealed partial class PointGroupDialogViewModel : ObservableObject
{
    public PointGroupDialogViewModel(
        bool isEdit,
        PointsConfig.PointGroupEntry? current,
        string deviceId,
        string? defaultCode = null)
    {
        IsEdit = isEdit;
        _deviceId = deviceId;
        DialogTitle = isEdit ? "编辑点位分组" : "新增点位分组";
        DialogSubtitle = "分组只用于树状组织和筛选，不参与驱动寻址、运行时路由或实时值存储。";
        if (current is null)
        {
            Code = defaultCode ?? "GROUP_1";
            Name = "新分组";
            SortOrderText = "10";
        }
        else
        {
            _groupId = current.Id;
            _deviceId = current.DeviceId;
            Code = current.Code;
            Name = current.Name;
            SortOrderText = current.SortOrder.ToString(CultureInfo.InvariantCulture);
            Description = current.Description;
        }
    }

    public bool IsEdit { get; }
    public string DialogTitle { get; }
    public string DialogSubtitle { get; }
    public string DeviceId => _deviceId;
    public string DeviceHelpText => $"所属设备：{_deviceId}；保存前会检查同一设备内编码唯一。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _sortOrderText = "0";

    [ObservableProperty]
    private string _description = string.Empty;

    private string _groupId = string.Empty;
    private string _deviceId;
    private string _validationMessage = string.Empty;

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(Code)
        && !string.IsNullOrWhiteSpace(Name)
        && int.TryParse(SortOrderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sortOrder)
        && sortOrder >= 0;

    public bool TryBuildResult(out PointGroupDialogResult result)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code)) errors.Add("分组编码不能为空");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("分组名称不能为空");
        if (string.IsNullOrWhiteSpace(_deviceId)) errors.Add("分组所属设备不能为空");
        if (!int.TryParse(SortOrderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sortOrder)
            || sortOrder < 0)
            errors.Add("排序号必须是大于等于 0 的整数");

        if (errors.Count > 0)
        {
            ValidationMessage = string.Join("；", errors);
            result = null!;
            return false;
        }

        ValidationMessage = string.Empty;
        result = new PointGroupDialogResult(
            _groupId,
            _deviceId,
            Code.Trim(),
            Name.Trim(),
            sortOrder,
            Description.Trim());
        return true;
    }
}
