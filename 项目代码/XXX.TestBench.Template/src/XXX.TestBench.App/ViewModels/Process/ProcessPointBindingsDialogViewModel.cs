using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels.Process;

public sealed record ProcessBindingPointChoice(
    PointsConfig.PointEntry Entry,
    string DeviceText,
    string QualityText,
    bool IsUnbound = false)
{
    public string PointId => IsUnbound ? string.Empty : Entry.Id;

    public string DisplayName
        => IsUnbound
            ? "未绑定（仅对应控件不可用）"
            : string.IsNullOrWhiteSpace(Entry.Name)
                ? Entry.Code
                : Entry.Name.Trim();

    public string DetailsText
        => IsUnbound
            ? "不会创建默认点位或默认值"
            : $"{Entry.Code} · {DeviceText} · {DevicePointTypeCatalog.ToDisplayName(Entry.RawDataTypeKind)} · {QualityText}";

    public bool IsCompatible(ProcessSignalDefinition definition)
        => IsUnbound
            || (definition.AcceptedDataTypes.Contains(Entry.RawDataTypeKind)
                && (!definition.IsWritable || Entry.IsWritable)
                && (!definition.RequiresEngineeringRange
                    || (Entry.EffectiveEngMin.HasValue
                        && Entry.EffectiveEngMax.HasValue
                        && Entry.EffectiveEngMin <= Entry.EffectiveEngMax)));
}

public sealed partial class ProcessBindingRow : ObservableObject
{
    public ProcessBindingRow(
        ProcessSignalDefinition definition,
        IEnumerable<ProcessBindingPointChoice> pointOptions,
        string? selectedPointId)
    {
        Definition = definition;
        foreach (var option in pointOptions)
            PointOptions.Add(option);
        SelectedPoint = PointOptions.FirstOrDefault(option =>
            string.Equals(option.PointId, selectedPointId?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? PointOptions.FirstOrDefault(option => option.IsUnbound);
    }

    public ProcessSignalDefinition Definition { get; }
    public string SignalKey => Definition.SignalKey;
    public string DisplayName => Definition.DisplayName;
    public string AccessText => Definition.IsWritable ? "写入" : "读取";
    public string RequirementText => Definition.RequirementText;
    public ObservableCollection<ProcessBindingPointChoice> PointOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private ProcessBindingPointChoice? _selectedPoint;

    public bool IsSelectedPointCompatible
        => SelectedPoint is null || SelectedPoint.IsCompatible(Definition);

    public string StatusText
    {
        get
        {
            if (SelectedPoint is null || SelectedPoint.IsUnbound)
                return "未绑定；仅对应控件不可用";
            return SelectedPoint.IsCompatible(Definition)
                ? "已建立关联"
                : "类型、写入能力或量程不匹配";
        }
    }

    partial void OnSelectedPointChanged(ProcessBindingPointChoice? value)
        => OnPropertyChanged(nameof(IsSelectedPointCompatible));
}

/// <summary>
/// 工艺点位绑定窗口模型。只提交工艺目录中的键和打开窗口时记录的配置版本。
/// </summary>
public sealed partial class ProcessPointBindingsDialogViewModel : ObservableObject
{
    private readonly DeviceConfigurationService _configurationService;
    private readonly UserContext _actor;
    private readonly string _expectedRevision;

    public ProcessPointBindingsDialogViewModel(
        IEnumerable<ProcessSignalDefinition> definitions,
        IEnumerable<PointsConfig.PointEntry> points,
        IEnumerable<DeviceConfig.DeviceEntry> devices,
        IReadOnlyDictionary<string, string>? currentBindings,
        string currentRevision,
        DeviceConfigurationService configurationService,
        UserContext actor,
        IDeviceRuntime? runtime = null)
    {
        _configurationService = configurationService;
        _actor = actor;
        _expectedRevision = currentRevision?.Trim() ?? string.Empty;
        CurrentRevision = string.IsNullOrWhiteSpace(_expectedRevision) ? "未读取" : "已加载当前配置";

        var pointOptions = (points ?? Array.Empty<PointsConfig.PointEntry>())
            .Where(point => point is not null && !string.IsNullOrWhiteSpace(point.Id))
            .Select(point => new ProcessBindingPointChoice(
                point,
                ResolveDeviceText(point, devices),
                ResolveQualityText(point.Id, runtime)))
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Entry.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        pointOptions.Insert(0, new ProcessBindingPointChoice(
            new PointsConfig.PointEntry { Id = string.Empty, Code = "未绑定", Name = "未绑定" },
            string.Empty,
            "未读取",
            IsUnbound: true));

        var bindings = currentBindings
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in (definitions ?? Array.Empty<ProcessSignalDefinition>())
                     .Where(definition => definition is not null)
                     .GroupBy(definition => definition.SignalKey, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(definition => definition.SignalKey, StringComparer.OrdinalIgnoreCase))
        {
            bindings.TryGetValue(definition.SignalKey, out var pointId);
            Rows.Add(new ProcessBindingRow(definition, pointOptions, pointId));
        }
    }

    public ObservableCollection<ProcessBindingRow> Rows { get; } = new();
    public string CurrentRevision { get; }
    public string HintText
        => "点位名称优先显示；未绑定只影响对应控件。保存时服务端会再次校验类型、写入能力、工程量程和配置版本。";

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

    public async Task<DeviceConfigurationApplyResult> SaveAsync(
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        var invalid = Rows.FirstOrDefault(row => !row.IsSelectedPointCompatible);
        if (invalid is not null)
        {
            ValidationMessage = $"“{invalid.DisplayName}”的绑定不满足工艺点位要求";
            return new DeviceConfigurationApplyResult(false, string.Empty, ValidationMessage);
        }

        if (string.IsNullOrWhiteSpace(_expectedRevision))
        {
            ValidationMessage = "未读取当前配置版本，不能保存";
            return new DeviceConfigurationApplyResult(false, string.Empty, ValidationMessage);
        }

        IsBusy = true;
        ValidationMessage = string.Empty;
        try
        {
            var patch = Rows.ToDictionary(
                row => row.SignalKey,
                row => row.SelectedPoint is null || row.SelectedPoint.IsUnbound
                    ? null
                    : row.SelectedPoint.PointId.Trim(),
                StringComparer.OrdinalIgnoreCase);
            var result = await _configurationService.ApplySignalBindingPatchAsync(
                _actor,
                patch,
                _expectedRevision,
                ct,
                s7OptimizedBlockAccessConfirmed);
            if (!result.Ok)
                ValidationMessage = result.Error ?? "工艺点位绑定应用失败";
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

    private static string ResolveDeviceText(
        PointsConfig.PointEntry point,
        IEnumerable<DeviceConfig.DeviceEntry> devices)
    {
        var device = XXX.TestBench.App.ViewModels.DeviceStationDisplay.ResolveDevice(devices, point.DeviceId);
        return XXX.TestBench.App.ViewModels.DeviceStationDisplay.FormatDeviceText(device);
    }

    private static string ResolveQualityText(string pointId, IDeviceRuntime? runtime)
    {
        if (runtime is null || !runtime.TryGetCachedValue(pointId, out var value))
            return "未读取";
        return value.Quality switch
        {
            PointQuality.Good => "正常",
            PointQuality.Stale => "陈旧",
            PointQuality.Bad => "无效",
            _ => "未读取"
        };
    }
}
