namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 配置读写边界。保存采用临时文件、校验、原子替换和可恢复前一版本。
/// </summary>
public interface IConfigStore
{
    Task<T> LoadAsync<T>(string relativePath, CancellationToken ct = default) where T : class;
    Task SaveAsync<T>(string relativePath, T config, CancellationToken ct = default) where T : class;
}
