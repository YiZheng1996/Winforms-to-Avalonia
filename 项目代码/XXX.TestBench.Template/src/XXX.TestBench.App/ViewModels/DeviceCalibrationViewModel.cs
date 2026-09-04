using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备与校准页面：展示设备状态与点位读取；写入走安全链（校准或高风险需权限加确认）。
/// </summary>
public sealed partial class DeviceCalibrationViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public DeviceCalibrationViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "设备与校准";

    /// <summary>
    /// 页面展示的点位列表。
    /// </summary>
    public ObservableCollection<PointRow> Points { get; } = new();

    private string _deviceStatus = string.Empty;

    /// <summary>
    /// 设备当前状态的显示文字。
    /// </summary>
    public string DeviceStatus { get => _deviceStatus; private set => SetProperty(ref _deviceStatus, value); }

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
    /// 是否已完成校准写入确认。
    /// </summary>
    [ObservableProperty]
    private bool _confirmed;

    /// <summary>
    /// 页面加载命令：读取设备状态与点位列表并刷新实时值。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var runtime = _services.DeviceModes.Runtime;
            DeviceStatus = runtime is null ? "未初始化" : $"{runtime.Name}（{runtime.Status.Health} / {(runtime.Status.IsConnected ? "已连接" : "未连接")}）";
            Points.Clear();
            if (runtime is null) return;
            foreach (var p in await runtime.ListPointsAsync(ct))
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
    /// 校准命令：把输入值写入当前选中的点位。
    /// </summary>
    [RelayCommand]
    public async Task CalibrateAsync() => await CalibrateSelectedPointAsync(SelectedPoint);

    private async Task CalibrateSelectedPointAsync(PointRow? row)
    {
        StatusMessage = string.Empty;
        try
        {
            if (row is null || !row.IsWritable) throw new Core.Common.DomainException("请选择可写点位");
            var runtime = _services.DeviceModes.Runtime ?? throw new Core.Common.DomainException("设备运行时未初始化");
            var point = (await runtime.ListPointsAsync()).FirstOrDefault(p => p.Code == row.Code) ?? throw new Core.Common.DomainException("点位不存在");
            object? value = bool.TryParse(WriteValue, out var b) ? b : (decimal.TryParse(WriteValue, out var d) ? d : WriteValue);
            var activeRun = await _services.RecordRepository.GetActiveRunningRecordAsync() is not null;
            await _services.WritePipeline.ExecuteAsync(new WriteCommand(_actor, point, value, _services.DeviceConfig.DeviceMode, runtime, activeRun, Confirmed));
            StatusMessage = $"已写入 {row.Code}={value}";
            await RefreshAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}