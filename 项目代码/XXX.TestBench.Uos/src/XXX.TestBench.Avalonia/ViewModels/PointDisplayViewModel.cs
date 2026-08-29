using System.Globalization;
using XXX.TestBench.Gateway.Domain;

namespace XXX.TestBench.Avalonia.ViewModels;

public sealed class PointDisplayViewModel : ObservableObject
{
    private object? _value;
    private DataQuality _quality;
    private DateTimeOffset _timestamp;
    private long _connectionGeneration;
    private string? _diagnostic;

    public PointDisplayViewModel(GatewayPointDefinition definition)
    {
        PointId = definition.LogicalPoint;
        Description = definition.Description;
        ValueType = definition.ValueType;
        _quality = DataQuality.Unknown;
        _timestamp = DateTimeOffset.UtcNow;
        _diagnostic = "等待首个采样";
    }

    public string PointId { get; }
    public string Description { get; }
    public GatewayPointValueType ValueType { get; }
    public object? Value => _value;
    public DataQuality Quality => _quality;
    public DateTimeOffset Timestamp => _timestamp;
    public long ConnectionGeneration => _connectionGeneration;
    public string? Diagnostic => _diagnostic;
    public string DisplayValue => FormatValue(_value);
    public string QualityText => _quality switch
    {
        DataQuality.Good => "Good",
        DataQuality.Uncertain => "不确定",
        DataQuality.Bad => "Bad",
        _ => "通信未知"
    };
    public string QualityColor => _quality switch
    {
        DataQuality.Good => "#16803C",
        DataQuality.Uncertain => "#9A6700",
        DataQuality.Bad => "#C62828",
        _ => "#6B7280"
    };
    public string DetailText => string.IsNullOrWhiteSpace(_diagnostic)
        ? $"采样时间 {Timestamp.LocalDateTime:HH:mm:ss.fff}"
        : $"{_diagnostic} · {Timestamp.LocalDateTime:HH:mm:ss.fff}";

    public void Apply(GatewayPointSample sample)
    {
        _value = sample.Value;
        _quality = sample.Quality;
        _timestamp = sample.Timestamp;
        _connectionGeneration = sample.ConnectionGeneration;
        _diagnostic = sample.Diagnostic;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(Timestamp));
        OnPropertyChanged(nameof(ConnectionGeneration));
        OnPropertyChanged(nameof(Diagnostic));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(QualityText));
        OnPropertyChanged(nameof(QualityColor));
        OnPropertyChanged(nameof(DetailText));
    }

    private string FormatValue(object? value) => value switch
    {
        null => "—",
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "—"
    };
}
