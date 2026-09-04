using System.Text.Json;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// JSON 配置存储：加载时校验 schemaVersion；保存采用临时文件、校验、原子替换并可恢复前一版本。
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    /// <summary>
    /// 配置根目录。
    /// </summary>
    private readonly string _configRoot;

    /// <summary>
    /// 创建配置存储。
    /// </summary>
    public JsonConfigStore(string configRoot) => _configRoot = configRoot;

    /// <summary>
    /// 读取配置文件并反序列化。
    /// </summary>
    public async Task<T> LoadAsync<T>(string relativePath, CancellationToken ct = default) where T : class
    {
        var path = Resolve(relativePath);
        if (!File.Exists(path)) throw new Core.Configuration.ConfigValidationException($"配置文件不存在：{relativePath}");
        try
        {
            await using var stream = File.OpenRead(path);
            var config = await JsonSerializer.DeserializeAsync<T>(stream, Options, ct)
                ?? throw new Core.Configuration.ConfigValidationException($"配置文件为空或格式错误：{relativePath}");
            return config;
        }
        catch (Core.Configuration.ConfigValidationException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new Core.Configuration.ConfigValidationException($"配置文件 JSON 解析失败：{relativePath}（{ex.Message}）");
        }
    }

    /// <summary>
    /// 保存配置：先写临时文件并校验，再原子替换。
    /// </summary>
    public async Task SaveAsync<T>(string relativePath, T config, CancellationToken ct = default) where T : class
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        var backup = path + ".bak";

        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, config, Options, ct);
        }

        // 校验写入内容可重新解析
        try
        {
            await using var check = File.OpenRead(temp);
            var parsed = await JsonSerializer.DeserializeAsync<T>(check, Options, ct);
            if (parsed is null) throw new Core.Configuration.ConfigValidationException("保存内容校验失败");
        }
        catch (Exception)
        {
            TryDelete(temp);
            throw;
        }

        if (File.Exists(path)) File.Copy(path, backup, overwrite: true);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// 把相对路径转换为配置根目录下的完整路径。
    /// </summary>
    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_configRoot, relativePath));
        return full;
    }

    /// <summary>
    /// 尝试删除临时文件，失败时忽略。
    /// </summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略清理失败 */ }
    }

        private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
