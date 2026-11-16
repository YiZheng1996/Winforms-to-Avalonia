using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Domain.Execution;

namespace XXX.TestBench.App.ViewModels;

/// <summary>仅展示型号已保存的准备指导，不记录准备确认。</summary>
public sealed partial class PreparationGuidancePreviewViewModel : ObservableObject, IDisposable
{
    private readonly List<Bitmap> _bitmaps = [];

    public PreparationGuidancePreviewViewModel(ModelPreparationGuidanceSnapshot snapshot)
    {
        GuidanceTitle = $"{snapshot.Content.GuidanceId} · 版本 {snapshot.Content.Version}";
        ResourceSummary = $"资源摘要 SHA-256：{snapshot.Sha256}";
        Steps = snapshot.Content.Steps.Select((step, index) =>
        {
            Bitmap? image = null;
            if (step.ImageKey is not null && snapshot.Images.TryGetValue(step.ImageKey, out var bytes))
            {
                using var stream = new MemoryStream(bytes.ToArray(), writable: false);
                image = new Bitmap(stream);
                _bitmaps.Add(image);
            }

            return new PreviewStep(index + 1, step.Title, step.Text,
                step.RequiresConfirmation ? "本步骤需要确认" : "阅读步骤", image);
        }).ToArray();
    }

    public string GuidanceTitle { get; }
    public string ResourceSummary { get; }
    public IReadOnlyList<PreviewStep> Steps { get; }

    public void Dispose()
    {
        foreach (var bitmap in _bitmaps)
            bitmap.Dispose();
        _bitmaps.Clear();
    }

    public sealed record PreviewStep(int Number, string Title, string Text, string ConfirmationText, Bitmap? Image)
    {
        public bool HasImage => Image is not null;
    }
}
