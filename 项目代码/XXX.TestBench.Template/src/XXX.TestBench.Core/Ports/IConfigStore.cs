namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 配置读写边界。保存采用临时文件、校验、原子替换和可恢复前一版本。
/// </summary>
public interface IConfigStore
{
    /// <summary>
    /// 从配置文件读取对象；读取或解析失败时直接报错。
    /// </summary>
    Task<T> LoadAsync<T>(string relativePath, CancellationToken ct = default) where T : class;
    /// <summary>
    /// 校验后原子保存配置文件。
    /// </summary>
    Task SaveAsync<T>(string relativePath, T config, CancellationToken ct = default) where T : class;
}
