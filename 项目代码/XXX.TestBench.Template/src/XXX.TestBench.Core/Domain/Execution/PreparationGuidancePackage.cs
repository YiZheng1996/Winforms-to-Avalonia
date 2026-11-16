using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>
/// 检查离线准备指导包。包内包含 guidance.json 和其引用的 PNG/JPEG 图片，拒绝越界路径和未引用文件。
/// </summary>
public static class PreparationGuidancePackage
{
    public const int MaxPackageBytes = 32 * 1024 * 1024;
    public const int MaxExpandedBytes = 64 * 1024 * 1024;
    public const int MaxImageBytes = 8 * 1024 * 1024;
    private const int MaxJsonBytes = 1024 * 1024;
    private const int MaxEntries = 65;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>验证压缩包、JSON 步骤和图片引用，返回已核对的步骤内容。</summary>
    public static async Task<PreparationGuidanceContentV1> ValidateAsync(
        ReadOnlyMemory<byte> packageBytes, CancellationToken cancellationToken = default)
        => (await ReadValidatedAsync(packageBytes, cancellationToken)).Content;

    /// <summary>验证指导包并返回步骤及已校验的图片字节。</summary>
    public static async Task<PreparationGuidancePackageData> ReadValidatedAsync(
        ReadOnlyMemory<byte> packageBytes, CancellationToken cancellationToken = default)
    {
        if (packageBytes.IsEmpty || packageBytes.Length > MaxPackageBytes)
            throw new InvalidDataException("准备指导 ZIP 包为空或超过 32 MB。");

        using var source = new MemoryStream(packageBytes.ToArray(), writable: false);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count is 0 or > MaxEntries)
            throw new InvalidDataException("准备指导包必须包含 1 到 65 个文件。");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[]? jsonBytes = null;
        long expandedTotal = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName;
            ValidateEntryName(name);
            if (!names.Add(name))
                throw new InvalidDataException($"准备指导包包含重复路径：{name}");

            if (name.EndsWith("/", StringComparison.Ordinal))
            {
                if (!string.Equals(name, "images/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"准备指导包包含未允许的目录：{name}");
                continue;
            }

            if (entry.Length <= 0 || entry.Length > MaxExpandedBytes)
                throw new InvalidDataException($"准备指导包文件长度无效：{name}");
            expandedTotal = checked(expandedTotal + entry.Length);
            if (expandedTotal > MaxExpandedBytes)
                throw new InvalidDataException("准备指导包解压后超过 64 MB。");

            if (string.Equals(name, "guidance.json", StringComparison.OrdinalIgnoreCase))
            {
                if (entry.Length > MaxJsonBytes)
                    throw new InvalidDataException("guidance.json 超过 1 MB。");
                jsonBytes = await ReadEntryAsync(entry, MaxJsonBytes, cancellationToken);
                continue;
            }

            if (!name.StartsWith("images/", StringComparison.OrdinalIgnoreCase)
                || !IsSupportedImageExtension(name))
                throw new InvalidDataException($"准备指导包只允许 guidance.json 和 images/ 下的 PNG/JPEG 文件：{name}");
            if (entry.Length > MaxImageBytes)
                throw new InvalidDataException($"准备指导图片超过 8 MB：{name}");
            var imageBytes = await ReadEntryAsync(entry, MaxImageBytes, cancellationToken);
            ValidateImageSignature(name, imageBytes);
            images.Add(name, imageBytes);
        }

        if (jsonBytes is null)
            throw new InvalidDataException("准备指导包缺少 guidance.json。");
        if (jsonBytes.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble))
            throw new InvalidDataException("guidance.json 必须使用无 BOM 的 UTF-8。");

        PreparationGuidanceContentV1 content;
        try
        {
            content = JsonSerializer.Deserialize<PreparationGuidanceContentV1>(jsonBytes, JsonOptions)
                ?? throw new InvalidDataException("guidance.json 内容为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("guidance.json 格式无效：" + ex.Message, ex);
        }

        ValidateContent(content, images.Keys);
        var readOnlyImages = images.ToDictionary(
            pair => pair.Key,
            pair => (ReadOnlyMemory<byte>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
        return new PreparationGuidancePackageData(content, readOnlyImages);
    }

    private static void ValidateContent(PreparationGuidanceContentV1 content, IEnumerable<string> imageNames)
    {
        if (content.SchemaVersion != 1)
            throw new InvalidDataException($"不支持的准备指导版本：{content.SchemaVersion}。");
        if (string.IsNullOrWhiteSpace(content.GuidanceId) || string.IsNullOrWhiteSpace(content.Version))
            throw new InvalidDataException("准备指导必须包含 GuidanceId 和 Version。");
        if (content.Steps is null || content.Steps.Count == 0 || content.Steps.Count > 200)
            throw new InvalidDataException("准备指导步骤数量必须为 1 到 200。");
        if (content.Steps.Any(step => step is null
            || string.IsNullOrWhiteSpace(step.StepId)
            || string.IsNullOrWhiteSpace(step.Title)
            || string.IsNullOrWhiteSpace(step.Text)))
            throw new InvalidDataException("每个准备步骤都必须包含 StepId、Title 和 Text。");
        if (content.Steps.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != content.Steps.Count)
            throw new InvalidDataException("准备指导步骤 StepId 不能重复。");

        var referencedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in content.Steps)
        {
            if (step.ImageKey is null) continue;
            ValidateEntryName(step.ImageKey);
            if (!step.ImageKey.StartsWith("images/", StringComparison.OrdinalIgnoreCase)
                || !IsSupportedImageExtension(step.ImageKey))
                throw new InvalidDataException($"准备指导图片键无效：{step.ImageKey}");
            referencedImages.Add(step.ImageKey);
        }

        var actualImages = imageNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!referencedImages.SetEquals(actualImages))
            throw new InvalidDataException("准备指导的图片键必须与包内 PNG/JPEG 文件一一对应。");
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.StartsWith("/", StringComparison.Ordinal)
            || name.Contains(':') || name.Any(char.IsControl))
            throw new InvalidDataException("准备指导包包含无效路径。");
        foreach (var segment in name.Split('/'))
            if (segment is ".." or ".")
                throw new InvalidDataException("准备指导包路径不能包含 . 或 .. 片段。");
    }

    private static bool IsSupportedImageExtension(string name)
    {
        const string imagePrefix = "images/";
        if (!name.StartsWith(imagePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var imageName = name[imagePrefix.Length..];
        if (imageName.Length == 0 || imageName.Contains('/'))
            return false;

        var extension = Path.GetExtension(imageName);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateImageSignature(string name, byte[] bytes)
    {
        var extension = Path.GetExtension(name);
        var isPng = extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var isJpeg = (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                      || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            && bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        if (!isPng && !isJpeg)
            throw new InvalidDataException($"图片扩展名与文件格式不匹配：{name}");
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry, int maxBytes, CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        using var output = new MemoryStream((int)Math.Min(entry.Length, maxBytes));
        var buffer = new byte[81920];
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (output.Length + count > maxBytes)
                throw new InvalidDataException($"准备指导文件超过允许大小：{entry.FullName}");
            output.Write(buffer, 0, count);
        }
        if (output.Length != entry.Length)
            throw new InvalidDataException($"准备指导文件解压长度不符：{entry.FullName}");
        return output.ToArray();
    }
}
