using XXX.TestBench.Core.Domain.TestDefinitions;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 试验项定义只读边界。模板执行顺序由代码固定并由初始化种子化到数据库，运行时不再新增或编辑试验项。
/// </summary>
public interface ITestDefinitionRepository
{
    Task<TestItemDefinition?> GetItemAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<TestItemDefinition>> ListItemsAsync(bool includeDisabled, CancellationToken ct = default);
}
