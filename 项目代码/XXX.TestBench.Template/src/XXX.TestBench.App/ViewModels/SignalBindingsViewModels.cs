using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Execution;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 信号绑定下拉框中的设备点位选项。显示设备归属，避免同地址点位被误选到另一台设备。
/// </summary>
public sealed record SignalBindingPointChoice(
    PointsConfig.PointEntry Entry,
    string DeviceCode,
    bool IsUnbound = false)
{
    public string PointId => IsUnbound ? string.Empty : Entry.Id;

    public string DisplayName
        => IsUnbound ? "未绑定（清除当前绑定）" : $"{DeviceCode} · {Entry.Code}  {Entry.Name}";

    public string DetailsText
        => IsUnbound ? "开始试验前会因缺少必需信号而被拒绝" : $"{DevicePointTypeCatalog.ToDisplayName(Entry.RawDataTypeKind, Entry.RawDataType)}"
           + " · 配置点位";

    public bool IsCompatible(RequiredSignal requirement)
        => IsUnbound
           || (IsTypeCompatible(requirement.ExpectedDataType, Entry.RawDataTypeKind)
           && (requirement.Access != SignalAccessKind.Write || Entry.IsWritable));

    private static bool IsTypeCompatible(DevicePointDataType expected, DevicePointDataType actual)
        => expected == actual
           || (expected is DevicePointDataType.Boolean or DevicePointDataType.Bool
               && actual is DevicePointDataType.Boolean or DevicePointDataType.Bool);

    public override string ToString() => DisplayName;
}

/// <summary>
/// 一个固定 SignalKey 的编辑行。用户只能从当前配置点位中选择 PointId，不能在界面生成新的执行逻辑键。
/// </summary>
public sealed partial class SignalBindingRow : ObservableObject
{
    public SignalBindingRow(
        RequiredSignal requirement,
        IEnumerable<SignalBindingPointChoice> pointOptions,
        string? selectedPointId)
    {
        Requirement = requirement;
        foreach (var point in pointOptions)
            PointOptions.Add(point);
        SelectedPoint = PointOptions.FirstOrDefault(point =>
            string.Equals(point.PointId, selectedPointId?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? PointOptions.FirstOrDefault(point => point.IsUnbound);
    }

    public RequiredSignal Requirement { get; }

    public string SignalKey => Requirement.SignalKey;

    public string AccessText => Requirement.Access == SignalAccessKind.Read ? "读取" : "写入";

    public string RequirementText
        => $"{DevicePointTypeCatalog.ToDisplayName(Requirement.ExpectedDataType)}"
           + (string.IsNullOrWhiteSpace(Requirement.Unit) ? string.Empty : $" · {Requirement.Unit}")
           + $" · 样本不超过 {Requirement.MaxSampleAge.TotalSeconds:0.###} 秒";

    public ObservableCollection<SignalBindingPointChoice> PointOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private SignalBindingPointChoice? _selectedPoint;

    public bool HasSelectedPoint => SelectedPoint is not null;

    public bool IsSelectedPointCompatible
        => SelectedPoint is null || SelectedPoint.IsCompatible(Requirement);

    public string StatusText
    {
        get
        {
            if (SelectedPoint is null || SelectedPoint.IsUnbound)
                return "未绑定；开始试验前会被拒绝";
            if (!SelectedPoint.IsCompatible(Requirement))
                return "类型或写入能力不匹配；保存会被拒绝";
            return "已建立关联";
        }
    }

    partial void OnSelectedPointChanged(SignalBindingPointChoice? value)
    {
        OnPropertyChanged(nameof(HasSelectedPoint));
        OnPropertyChanged(nameof(IsSelectedPointCompatible));
    }
}

/// <summary>
/// 项目级信号绑定编辑器。保存仍通过完整设备配置应用服务完成，失败时不关闭窗口。
/// </summary>
public sealed partial class SignalBindingsDialogViewModel : ObservableObject
{
    private readonly DeviceConfigurationService _configurationService;
    private readonly UserContext _actor;

    public SignalBindingsDialogViewModel(
        IEnumerable<RequiredSignal> requirements,
        IEnumerable<PointsConfig.PointEntry> points,
        IEnumerable<DeviceConfig.DeviceEntry> devices,
        IReadOnlyDictionary<string, string>? currentBindings,
        string currentRevision,
        DeviceConfigurationService configurationService,
        UserContext actor)
    {
        _configurationService = configurationService;
        _actor = actor;
        CurrentRevision = string.IsNullOrWhiteSpace(currentRevision) ? "未读取" : "已加载";

        var deviceCodeById = (devices ?? Array.Empty<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null && !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.IsNullOrWhiteSpace(group.First().Code)
                    ? group.First().Name.Trim()
                    : group.First().Code.Trim(),
                StringComparer.OrdinalIgnoreCase);
        var pointOptions = (points ?? Array.Empty<PointsConfig.PointEntry>())
            .Where(point => point is not null && !string.IsNullOrWhiteSpace(point.Id))
            .Select(point => new SignalBindingPointChoice(
                point,
                ResolveDeviceCode(point, deviceCodeById)))
            .OrderBy(point => point.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.Entry.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        pointOptions.Insert(0, new SignalBindingPointChoice(
            new PointsConfig.PointEntry { Id = string.Empty, Code = "未绑定", Name = "未绑定" },
            string.Empty,
            IsUnbound: true));
        var bindings = currentBindings ?? new Dictionary<string, string>();

        foreach (var requirement in (requirements ?? Array.Empty<RequiredSignal>())
                     .Where(requirement => requirement is not null
                         && !string.IsNullOrWhiteSpace(requirement.SignalKey))
                     .GroupBy(requirement => requirement.SignalKey.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(requirement => requirement.SignalKey, StringComparer.OrdinalIgnoreCase))
        {
            var binding = bindings.FirstOrDefault(pair =>
                string.Equals(pair.Key?.Trim(), requirement.SignalKey.Trim(), StringComparison.OrdinalIgnoreCase));
            Rows.Add(new SignalBindingRow(requirement, pointOptions, binding.Value));
        }
    }

    public ObservableCollection<SignalBindingRow> Rows { get; } = new();

    public string CurrentRevision { get; }

    public string HintText
        => "业务信号由试验流程预先定义；未绑定或不匹配的必需信号会在保存或开始试验前被拦截。已配置点位均参与运行。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isBusy;

    private string _validationMessage = string.Empty;

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool CanSave => !IsBusy && Rows.Count > 0;

    /// <summary>
    /// 将当前选择保存为项目级 SignalKey → PointId 绑定。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> SaveAsync(
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        var invalid = Rows.FirstOrDefault(row =>
            row.SelectedPoint is not null && !row.IsSelectedPointCompatible);
        if (invalid is not null)
        {
            var message = "业务信号关联不满足类型或写入能力要求";
            ValidationMessage = message;
            return new DeviceConfigurationApplyResult(false, string.Empty, message);
        }

        IsBusy = true;
        ValidationMessage = string.Empty;
        try
        {
            var bindings = Rows
                .Where(row => row.SelectedPoint is not null && !row.SelectedPoint.IsUnbound)
                .ToDictionary(
                    row => row.SignalKey.Trim(),
                    row => row.SelectedPoint!.PointId.Trim(),
                    StringComparer.OrdinalIgnoreCase);
            var result = await _configurationService.ApplySignalBindingsAsync(
                _actor,
                new SignalBindingsConfig
                {
                    SchemaVersion = SignalBindingsConfig.CurrentSchemaVersion,
                    Bindings = bindings
                },
                ct,
                s7OptimizedBlockAccessConfirmed);
            if (!result.Ok)
                ValidationMessage = result.Error ?? "信号绑定应用失败";
            return result;
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
            return new DeviceConfigurationApplyResult(false, string.Empty, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string ResolveDeviceCode(
        PointsConfig.PointEntry point,
        IReadOnlyDictionary<string, string> deviceCodeById)
    {
        if (!string.IsNullOrWhiteSpace(point.DeviceCode))
            return point.DeviceCode.Trim();
        if (!string.IsNullOrWhiteSpace(point.DeviceId)
            && deviceCodeById.TryGetValue(point.DeviceId.Trim(), out var code)
            && !string.IsNullOrWhiteSpace(code))
            return code;
        return "未分配";
    }
}
