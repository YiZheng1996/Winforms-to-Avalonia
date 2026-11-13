using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 项目级业务信号到设备点位的固定绑定。
/// </summary>
public sealed class SignalBindingsConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ConfigurationIssue> Validate(ISet<string> pointIds)
    {
        var issues = new List<ConfigurationIssue>();
        if (SchemaVersion != CurrentSchemaVersion)
        {
            issues.Add(new("signal-bindings.schemaVersion", $"不受支持（期望 {CurrentSchemaVersion}）"));
            return issues;
        }

        foreach (var pair in Bindings ?? new Dictionary<string, string>())
        {
            var key = pair.Key?.Trim() ?? string.Empty;
            var pointId = pair.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                issues.Add(new("signal-bindings.bindings", "SignalKey 不能为空"));
            if (string.IsNullOrWhiteSpace(pointId))
                issues.Add(new($"signal-bindings.{key}", "PointId 不能为空"));
            else if (!Guid.TryParse(pointId, out _) || !pointIds.Contains(pointId))
                issues.Add(new($"signal-bindings.{key}", $"引用的 PointId 不存在：{pointId}"));
        }

        return issues;
    }
}
