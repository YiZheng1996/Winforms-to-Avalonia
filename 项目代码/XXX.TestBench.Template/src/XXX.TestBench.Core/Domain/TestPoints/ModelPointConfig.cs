namespace XXX.TestBench.Core.Domain.TestPoints;

/// <summary>
/// 产品型号的项点配置行：把产品类型下已启用的试验项点按顺序编排为该型号的自动试验序列。
/// </summary>
public sealed record ModelPointConfig(int ProductModelId, int TestItemPointId, int SortOrder);
