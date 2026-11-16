namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 型号资源不可变存储。读取必须同时匹配资源键和冻结摘要。
/// </summary>
public interface IModelResourceStore
{
    /// <summary>读取资源键当前唯一绑定的不可变内容摘要；多版本键属于配置错误。</summary>
    Task<string> ResolveUniqueHashAsync(string resourceKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以不可变内容版本导入资源并返回其 SHA-256。
    /// </summary>
    Task<string> ImportAsync(
        string resourceKey,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 打开指定资源键及摘要对应的内容；调用方负责释放流。
    /// </summary>
    Task<Stream> OpenReadAsync(
        string resourceKey,
        string expectedSha256,
        CancellationToken cancellationToken = default);
}
