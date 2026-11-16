using System.Text.Json;
using System.Text.Json.Serialization;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// Strict reader for the current deployed device configuration files.
/// Legacy conversion is intentionally kept out of the application process.
/// </summary>
internal static class CurrentConfigurationJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Deserialize<T>(ReadOnlySpan<byte> json, string sourcePath) where T : class
    {
        var expectedVersion = GetCurrentVersion(typeof(T));
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ConfigValidationException($"{sourcePath} 根对象必须是 JSON 对象");

            if (!TryGetProperty(root, "schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var version))
                throw new ConfigValidationException($"{sourcePath}.schemaVersion 必须显式提供整数版本号");
            if (version != expectedVersion)
                throw new UnsupportedCurrentConfigurationVersionException(
                    $"{sourcePath}.schemaVersion={version} 不受支持（当前版本为 {expectedVersion}，请使用离线升级工具）");

            ValidateNoKnownLegacyFields(root, typeof(T), sourcePath);
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new ConfigValidationException($"{sourcePath} 为空或格式错误");
        }
        catch (ConfigValidationException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            var location = string.IsNullOrWhiteSpace(ex.Path) ? sourcePath : sourcePath + ex.Path;
            throw new ConfigValidationException($"{location} JSON 无效或包含未知字段：{ex.Message}");
        }
    }

    private static int GetCurrentVersion(Type type)
    {
        if (type == typeof(DeviceConfig)) return DeviceConfig.CurrentSchemaVersion;
        if (type == typeof(PointsConfig)) return PointsConfig.CurrentSchemaVersion;
        if (type == typeof(SimulationConfig)) return SimulationConfig.CurrentSchemaVersion;
        if (type == typeof(SignalBindingsConfig)) return SignalBindingsConfig.CurrentSchemaVersion;
        throw new ArgumentException($"{type.Name} 不是当前设备配置文件类型", nameof(type));
    }

    private static void ValidateNoKnownLegacyFields(JsonElement root, Type type, string path)
    {
        if (type == typeof(DeviceConfig))
        {
            Reject(root, path, "deviceMode", "pollIntervalMs", "timeoutMs");
            if (TryGetProperty(root, "devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var device in devices.EnumerateArray())
                {
                    var itemPath = $"{path}.devices[{index++}]";
                    Reject(device, itemPath, "protocol", "address", "enabled");
                    if (TryGetProperty(device, "autoDemotion", out var demotion))
                        Reject(demotion, itemPath + ".autoDemotion", "discardWritesWhileDemoted");
                }
            }
            if (TryGetProperty(root, "channels", out var channels) && channels.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var channel in channels.EnumerateArray())
                {
                    if (TryGetProperty(channel, "tcp", out var tcp))
                        Reject(tcp, $"{path}.channels[{index}].tcp", "host", "port");
                    index++;
                }
            }
            return;
        }

        if (type == typeof(PointsConfig)
            && TryGetProperty(root, "points", out var points)
            && points.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var point in points.EnumerateArray())
                Reject(point, $"{path}.points[{index++}]", "rawDataType", "scale");
            return;
        }

        if (type == typeof(SimulationConfig))
        {
            Reject(root, path, "initialValues");
            RejectNested(root, "changeRules", path, "address");
            RejectNested(root, "faultInjectionScenarios", path, "address");
        }
    }

    private static void RejectNested(JsonElement root, string collectionName, string path, params string[] fields)
    {
        if (!TryGetProperty(root, collectionName, out var collection) || collection.ValueKind != JsonValueKind.Array)
            return;
        var index = 0;
        foreach (var item in collection.EnumerateArray())
            Reject(item, $"{path}.{collectionName}[{index++}]", fields);
    }

    private static void Reject(JsonElement element, string path, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var field in fields)
            if (TryGetProperty(element, field, out _))
                throw new ConfigValidationException($"{path}.{field} 是已废弃字段；请使用离线升级工具转换配置");
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}

internal sealed class UnsupportedCurrentConfigurationVersionException : Exception
{
    public UnsupportedCurrentConfigurationVersionException(string message) : base(message) { }
}
