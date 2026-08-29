using System.Text.Json;
using System.Text.Json.Serialization;

namespace XXX.TestBench.Gateway.Domain;

public sealed class GatewaySettingsDocument
{
    public int SchemaVersion { get; set; } = 1;
    public GatewayOptions Gateway { get; set; } = GatewayOptions.CreateDefault();
    public string FirstBusinessSlice { get; set; } = "PressureAdjustmentValveB11";

    public void Validate()
    {
        if (SchemaVersion != 1)
            throw new OptionsValidationException($"不支持的设置文件版本: {SchemaVersion}");
        if (!string.Equals(FirstBusinessSlice, "PressureAdjustmentValveB11", StringComparison.Ordinal))
            throw new OptionsValidationException("首条业务流程必须为 PressureAdjustmentValveB11。");
        Gateway.Validate();
    }
}

public static class GatewaySettingsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    public static GatewaySettingsDocument Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("设置文件路径不能为空。", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("找不到 Gateway 设置文件。", path);

        var document = JsonSerializer.Deserialize<GatewaySettingsDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new OptionsValidationException("Gateway 设置文件为空。");
        document.Validate();
        return document;
    }

    public static void Save(string path, GatewaySettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.tmp.{Environment.ProcessId}.{Guid.NewGuid():N}");
        var predecessorPath = Path.Combine(
            directory,
            $"{Path.GetFileName(fullPath)}.previous.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Environment.ProcessId}");

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                options: FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
                File.Copy(fullPath, predecessorPath, overwrite: false);

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
