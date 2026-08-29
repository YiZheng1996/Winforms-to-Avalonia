namespace XXX.TestBench.Core.B11;

public sealed record B11PressureAdjustmentLimits(
    double HighMinimumKpa,
    double HighMaximumKpa,
    double LowMinimumKpa,
    double LowMaximumKpa,
    int MaximumAttempts = 3)
{
    public void Validate()
    {
        if (double.IsNaN(HighMinimumKpa) || double.IsNaN(HighMaximumKpa) ||
            double.IsNaN(LowMinimumKpa) || double.IsNaN(LowMaximumKpa) ||
            HighMinimumKpa > HighMaximumKpa || LowMinimumKpa > LowMaximumKpa)
            throw new ArgumentException("B11 压力范围无效。", nameof(HighMinimumKpa));
        if (MaximumAttempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(MaximumAttempts));
    }
}

public enum B11AdjustmentDecision
{
    NeedsOperatorAdjustment,
    Passed,
    Failed
}

public sealed record B11PressureAdjustmentObservation(
    double HighSidePressureKpa,
    double LowSidePressureKpa,
    int Attempt);

public sealed record B11PressureAdjustmentResult(
    B11AdjustmentDecision Decision,
    string Diagnostic,
    string UploadValue,
    IReadOnlyDictionary<string, double> ReportValues)
{
    public bool IsTerminal => Decision is B11AdjustmentDecision.Passed or B11AdjustmentDecision.Failed;
}

/// <summary>
/// Pure decision contract extracted from the legacy B11 profile-pressure flow.
/// Device writes, operator dialogs and report persistence stay outside this class.
/// </summary>
public static class PressureAdjustmentWorkflow
{
    public static B11PressureAdjustmentResult Evaluate(
        B11PressureAdjustmentLimits limits,
        B11PressureAdjustmentObservation observation)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(observation);
        limits.Validate();

        if (observation.Attempt is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(observation), "B11 尝试次数无效。");

        var highInRange = observation.HighSidePressureKpa >= limits.HighMinimumKpa &&
                          observation.HighSidePressureKpa <= limits.HighMaximumKpa;
        var lowInRange = observation.LowSidePressureKpa >= limits.LowMinimumKpa &&
                         observation.LowSidePressureKpa <= limits.LowMaximumKpa;

        var reportValues = new Dictionary<string, double>
        {
            ["val8"] = observation.HighSidePressureKpa,
            ["val16"] = observation.LowSidePressureKpa
        };

        if (highInRange && lowInRange)
        {
            return new B11PressureAdjustmentResult(
                B11AdjustmentDecision.Passed,
                "高压侧和低压侧压力均在设定范围内。",
                $"高压侧={observation.HighSidePressureKpa:F1};低压侧={observation.LowSidePressureKpa:F1}",
                reportValues);
        }

        if (observation.Attempt >= limits.MaximumAttempts)
        {
            return new B11PressureAdjustmentResult(
                B11AdjustmentDecision.Failed,
                "达到最大调整次数，压力仍不在设定范围内。",
                string.Empty,
                reportValues);
        }

        var side = highInRange ? "低压侧" : lowInRange ? "高压侧" : "高压侧和低压侧";
        return new B11PressureAdjustmentResult(
            B11AdjustmentDecision.NeedsOperatorAdjustment,
            $"{side}压力不在设定范围内，等待操作员调整后重试。",
            string.Empty,
            reportValues);
    }
}
