using System.Collections.ObjectModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Tasks;

namespace XXX.TestBench.App.ViewModels;

public sealed class PointRow : ObservableObject
{
    public required string Code { get; init; }
    public required string Address { get; init; }
    public required bool IsWritable { get; init; }
    public required WriteRiskLevel RiskLevel { get; init; }
    private string _value = string.Empty;
    public string Value { get => _value; set => SetField(ref _value, value); }
    private string _quality = string.Empty;
    public string Quality { get => _quality; set => SetField(ref _quality, value); }
}

/// <summary>工艺监控：Simulation 点位只读实时值 + 受控手动写入（走 DeviceWritePipeline）。</summary>
public sealed class ProcessMonitorViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public ProcessMonitorViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        LoadCommand = new RelayCommand(() => LoadAsync());
        RefreshCommand = new RelayCommand(RefreshAsync);
        WriteCommand = new RelayCommand(WriteSelectedAsync);
    }

    public override string Title => "工艺监控";

    public ObservableCollection<PointRow> Points { get; } = new();

    private PointRow? _selectedPoint;
    public PointRow? SelectedPoint { get => _selectedPoint; set => SetField(ref _selectedPoint, value); }

    private string _writeValue = string.Empty;
    public string WriteValue { get => _writeValue; set => SetField(ref _writeValue, value); }

    private bool _riskConfirmed;
    public bool RiskConfirmed { get => _riskConfirmed; set => SetField(ref _riskConfirmed, value); }

    public RelayCommand LoadCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand WriteCommand { get; }

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

    public async Task WriteSelectedAsync()
    {
        await WriteAsync(SelectedPoint);
    }

    private async Task WriteAsync(PointRow? row)
    {
        StatusMessage = string.Empty;
        try
        {
            if (row is null || !row.IsWritable) throw new Core.Common.DomainException("请选择可写点位");
            var runtime = _services.DeviceModes.Runtime ?? throw new Core.Common.DomainException("设备运行时未初始化");
            var point = (await runtime.ListPointsAsync()).FirstOrDefault(p => p.Code == row.Code) ?? throw new Core.Common.DomainException("点位不存在");
            object? value = bool.TryParse(WriteValue, out var b) ? b : (decimal.TryParse(WriteValue, out var d) ? d : WriteValue);
            var activeRun = await _services.TaskRepository.GetActiveRunningAsync() is not null;
            await _services.WritePipeline.ExecuteAsync(new WriteCommand(_actor, point, value, _services.DeviceConfig.DeviceMode, runtime, activeRun, RiskConfirmed));
            StatusMessage = $"已写入 {row.Code}={value}";
            await RefreshAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }
}
