using System.Buffers;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 不依赖 Avalonia 的 PDF 页面读取和渲染边界。
/// </summary>
public interface IPdfPageRenderer
{
    Task<PdfDocumentInfo> GetDocumentInfoAsync(
        Stream pdf,
        CancellationToken cancellationToken = default);

    Task<PdfPageImage> RenderPageAsync(
        Stream pdf,
        int pageIndex,
        double scale,
        CancellationToken cancellationToken = default);
}

public sealed record PdfDocumentInfo(int PageCount);

public enum PdfPixelFormat
{
    Bgra32 = 0,
    Rgba32 = 1
}

/// <summary>
/// 调用方必须 Dispose 以释放像素缓冲区所有权。
/// </summary>
public sealed class PdfPageImage : IDisposable
{
    public PdfPageImage(int width, int height, int stride, PdfPixelFormat pixelFormat, IMemoryOwner<byte> pixels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (stride < checked(width * 4)) throw new ArgumentOutOfRangeException(nameof(stride));
        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        if (_pixels.Memory.Length < checked(stride * height))
        {
            _pixels.Dispose();
            throw new ArgumentException("像素缓冲区长度小于 stride × height", nameof(pixels));
        }
    }

    private IMemoryOwner<byte>? _pixels;
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public PdfPixelFormat PixelFormat { get; }
    public ReadOnlyMemory<byte> Pixels => (_pixels ?? throw new ObjectDisposedException(nameof(PdfPageImage))).Memory;

    public void Dispose() => Interlocked.Exchange(ref _pixels, null)?.Dispose();
}
