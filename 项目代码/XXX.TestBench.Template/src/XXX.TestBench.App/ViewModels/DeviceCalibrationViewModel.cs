using System.Collections.ObjectModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

/// <summary>设备与校准：设备状态、点位读取；写入走安全链（校准/高风险需权限+确认）。</summary>
public sealed class DeviceCalibrationViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public DeviceCalibrationViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        LoadCommand = new RelayCommand(() => LoadAsync());
        RefreshCommand = new RelayCommand(RefreshAsync);
        CalibrateCommand = new RelayCommand(CalibrateSelectedAsync);
    }

    public override string Title => "设备与校准";

    public ObservableCollection<PointRow> Points { get; } = new();

    private string _deviceStatus = string.Empty;
    public string DeviceStatus { get => _deviceStatus; private set => SetField(ref _deviceStatus, value); }

    private PointRow? _selectedPoint;
    public PointRow? SelectedPoint { get => _selectedPoint; set => SetField(ref _selectedPoint, value); }

    private string _writeValue = string.Empty;
    public string WriteValue { get => _writeValue; set => SetField(ref _writeValue, value); }

    private bool _confirmed;
    public bool Confirmed { get => _confirmed; set => SetField(ref _confirmed, value); }

    public RelayCommand LoadCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CalibrateCommand { get; }

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

    public async Task CalibrateSelectedAsync()
    {
        await CalibrateAsync(SelectedPoint);
    }

    private async Task CalibrateAsync(PointRow? row)
    {
        StatusMessage = string.Empty;
        try
        {
            if (row is null || !row.IsWritable) throw new Core.Common.DomainException("请选择可写点位");
            var runtime = _services.DeviceModes.Runtime ?? throw new Core.Common.DomainException("设备运行时未初始化");
            var point = (await runtime.ListPointsAsync()).FirstOrDefault(p => p.Code == row.Code) ?? throw new Core.Common.DomainException("点位不存在");
            object? value = bool.TryParse(WriteValue, out var b) ? b : (decimal.TryParse(WriteValue, out var d) ? d : WriteValue);
            var activeRun = await _services.TaskRepository.GetActiveRunningAsync() is not null;
            await _services.WritePipeline.ExecuteAsync(new WriteCommand(_actor, point, value, _services.DeviceConfig.DeviceMode, runtime, activeRun, Confirmed));
            StatusMessage = $"已写入 {row.Code}={value}";
            await RefreshAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}
