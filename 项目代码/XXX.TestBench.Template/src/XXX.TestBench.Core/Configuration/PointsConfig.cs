using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Configuration;

public sealed class PointsConfig
{
    public int SchemaVersion { get; set; }
    public List<PointEntry> Points { get; set; } = new();

    public const int CurrentSchemaVersion = 1;

    public sealed class PointEntry
    {
        public required string Code { get; set; }
        public required string Protocol { get; set; }
        public required string Address { get; set; }
        public required string DataType { get; set; }
        public string Unit { get; set; } = string.Empty;
        public bool IsWritable { get; set; }
        public WriteRiskLevel RiskLevel { get; set; } = WriteRiskLevel.Normal;
        public decimal? RawMin { get; set; }
        public decimal? RawMax { get; set; }
        public decimal? EngMin { get; set; }
        public decimal? EngMax { get; set; }

        public DevicePoint ToDomain() => new(Code, Protocol, Address, DataType, Unit, IsWritable, RiskLevel, RawMin, RawMax, EngMin, EngMax);
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"points.json schemaVersion={SchemaVersion} 不受支持（期望 {CurrentSchemaVersion}）");
        var codes = Points.Select(p => p.Code).ToList();
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Count)
            throw new ConfigValidationException("points.json 存在重复点位代码");
    }
}
