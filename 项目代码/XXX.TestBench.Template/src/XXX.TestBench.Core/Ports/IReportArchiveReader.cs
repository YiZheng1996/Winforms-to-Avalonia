namespace XXX.TestBench.Core.Ports;

/// <summary>恢复只读取已发布且所有文件摘要正确的版本；缺失返回 null，损坏必须报错。</summary>
public interface IReportArchiveReader
{
    Task<ReportStorageManifest?> ReadPublishedAsync(Guid revisionId, CancellationToken ct = default);
}
