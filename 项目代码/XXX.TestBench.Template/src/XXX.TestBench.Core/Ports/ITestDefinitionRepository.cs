using XXX.TestBench.Core.Domain.TestDefinitions;

namespace XXX.TestBench.Core.Ports;

public interface ITestDefinitionRepository
{
    Task<TestItemDefinition?> GetItemAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<TestItemDefinition>> ListItemsAsync(bool includeDisabled, CancellationToken ct = default);
    Task<IReadOnlyList<ParameterDefinition>> ListParametersAsync(int itemId, CancellationToken ct = default);
    Task<ParameterDefinition?> GetParameterAsync(int id, CancellationToken ct = default);
    Task AddItemAsync(TestItemDefinition item, CancellationToken ct = default);
    Task AddParameterAsync(ParameterDefinition parameter, CancellationToken ct = default);
    Task UpdateItemAsync(TestItemDefinition item, CancellationToken ct = default);
    Task UpdateParameterAsync(ParameterDefinition parameter, CancellationToken ct = default);
}
