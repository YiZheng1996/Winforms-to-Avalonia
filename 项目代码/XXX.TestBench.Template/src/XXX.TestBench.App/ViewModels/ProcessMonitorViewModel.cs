using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 工艺监控点位表格中的一行，值与质量可被界面刷新。
/// </summary>
public sealed partial class PointRow : ObservableObject
{
    public required string Code { get; init; }
    public required string Address { get; init; }
    public required bool IsWritable { get; init; }
    public required WriteRiskLevel RiskLevel { get; init; }

    /// <summary>
    /// 点位当前的实时值文字。
    /// </summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>
    /// 点位当前的数据质量文字。
    /// </summary>
    [ObservableProperty]
    private string _quality = string.Empty;
}

/// <summary>
/// 工艺监控页面：仿真点位只读实时值，以及走安全链的受控手动写入。
/// </summary>
public sealed partial class ProcessMonitorViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public ProcessMonitorViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "工艺监控";

    /// <summary>
    /// 页面展示的点位列表。
    /// </summary>
    public ObservableCollection<PointRow> Points { get; } = new();

    /// <summary>
    /// 当前选中的点位。
    /// </summary>
    [ObservableProperty]
    private PointRow? _selectedPoint;

    /// <summary>
    /// 操作员输入的要写入点位的值。
    /// </summary>
    [ObservableProperty]
    private string _writeValue = string.Empty;

    /// <summary>
    /// 高风险写入是否已经得到操作员确认。
    /// </summary>
    [ObservableProperty]
    private bool _riskConfirmed;

    /// <summary>
    /// 页面加载命令：读取点位列表并刷新一次实时值。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Points.Clear();
            var runtime = _services.DeviceModes.Runtime;
            if (runtime is null) { StatusMessage = "设备运行时未初始化"; return; }
            var points = await runtime.ListPointsAsync(ct);
            foreach (var p in points)
                Points.Add(new PointRow { Code = p.Code, Address = p.Address, IsWritable = p.IsWritable, RiskLevel = p.RiskLevel });
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 刷新命令：重新读取全部点位的实时值与质量。
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var runtime = _services.DeviceModes.Runtime;
        if (runtime is null) return;
        var points = await runtime.ListPointsAsync();
        foreach (var row in Points)
        {
            var point = points.FirstOrDefault(p => p.Code == row.Code);
            if (point is null) continue;
            var value = await runtime.ReadAsync(point);
            row.Value = value.Value?.ToString() ?? string.Empty;
            row.Quality = value.Quality.ToString();
        }
    }

    /// <summary>
    /// 写入命令：把输入值写入当前选中的点位。
    /// </summary>
    [RelayCommand]
    public async Task WriteAsync() => await WriteSelectedPointAsync(SelectedPoint);

    private async Task WriteSelectedPointAsync(PointRow? row)
    {
        StatusMessage = string.Empty;
        try
        {
            if (row is null || !row.IsWritable) throw new Core.Common.DomainException("请选择可写点位");
            var runtime = _services.DeviceModes.Runtime ?? throw new Core.Common.DomainException("设备运行时未初始化");
            var point = (await runtime.ListPointsAsync()).FirstOrDefault(p => p.Code == row.Code) ?? throw new Core.Common.DomainException("点位不存在");
            object? value = bool.TryParse(WriteValue, out var b) ? b : (decimal.TryParse(WriteValue, out var d) ? d : WriteValue);
            var activeRun = await _services.RecordRepository.GetActiveRunningRecordAsync() is not null;
            await _services.WritePipeline.ExecuteAsync(new WriteCommand(_actor, point, value, _services.DeviceConfig.DeviceMode, runtime, activeRun, RiskConfirmed));
            StatusMessage = $"已写入 {row.Code}={value}";
            await RefreshAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}