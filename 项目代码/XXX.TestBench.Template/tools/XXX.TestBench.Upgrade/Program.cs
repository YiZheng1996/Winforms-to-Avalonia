using System.Text.Json;

namespace XXX.TestBench.Upgrade;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var reportPath = ReadOption(args, "--report");
        UpgradeReport? report = null;
        var exitCode = 1;
        try
        {
            if (args.Length == 0)
                throw new UpgradeCommandException(2, "缺少命令");
            var command = args[0];
            var source = ReadRequiredOption(args, "--source");
            if (!Path.IsPathFullyQualified(source))
                throw new UpgradeCommandException(2, "--source 必须是绝对路径");
            ValidateReportPath(reportPath, source, command, ReadOption(args, "--output"));

            if (command is "check-db" or "convert-db")
            {
                var upgrader = new SqliteV7ToV8Upgrade();
                report = command == "check-db"
                    ? await upgrader.CheckAsync(source)
                    : await upgrader.ConvertAsync(source, ReadRequiredOption(args, "--output"));
                exitCode = report.Success ? 0 : report.FailureCode;
            }
            else if (command is "check-db-v8-to-v9" or "convert-db-v8-to-v9")
            {
                var upgrader = new SqliteV8ToV9Upgrade();
                report = command == "check-db-v8-to-v9"
                    ? await upgrader.CheckAsync(source)
                    : await upgrader.ConvertAsync(
                        source,
                        ReadRequiredOption(args, "--output"),
                        ReadOption(args, "--legacy-bench-id") ?? string.Empty);
                exitCode = report.Success ? 0 : report.FailureCode;
            }
            else if (command is "check-config" or "convert-config")
            {
                var upgrader = new ConfigurationUpgradeService();
                var sourceKind = ReadRequiredOption(args, "--source-kind");
                var pointer = ReadOption(args, "--pointer") ?? "active";
                report = command == "check-config"
                    ? await upgrader.CheckAsync(source, sourceKind, pointer)
                    : await upgrader.ConvertAsync(source, sourceKind, ReadRequiredOption(args, "--output"), pointer);
                exitCode = report.Success ? 0 : report.FailureCode;
            }
            else
            {
                throw new UpgradeCommandException(2, $"未知命令：{command}");
            }
        }
        catch (UpgradeCommandException ex)
        {
            exitCode = ex.ExitCode;
            report ??= new UpgradeReport { Command = args.FirstOrDefault() ?? "invalid-command", SourcePath = ReadOption(args, "--source") };
            report.Blockers.Add(ex.Message);
            report.FailureCode = ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            exitCode = 1;
            report ??= new UpgradeReport { Command = args.FirstOrDefault() ?? "cancelled", SourcePath = ReadOption(args, "--source") };
            report.Blockers.Add("操作已取消");
            report.FailureCode = exitCode;
        }
        catch (IOException ex)
        {
            exitCode = 5;
            report ??= new UpgradeReport { Command = args.FirstOrDefault() ?? "io-error", SourcePath = ReadOption(args, "--source") };
            report.Blockers.Add(ex.Message);
            report.FailureCode = exitCode;
        }
        catch (Exception ex)
        {
            exitCode = 1;
            report ??= new UpgradeReport { Command = args.FirstOrDefault() ?? "failed", SourcePath = ReadOption(args, "--source") };
            report.Blockers.Add($"{ex.GetType().Name}: {ex.Message}");
            report.FailureCode = exitCode;
        }

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            if (!Path.IsPathFullyQualified(reportPath))
            {
                report.Blockers.Add("--report 必须是绝对路径");
                report.Success = false;
                report.FailureCode = 2;
                exitCode = 2;
            }
            else
            {
            var fullReportPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
            await File.WriteAllTextAsync(fullReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        if (report is not null)
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return exitCode;
    }

    private static string ReadRequiredOption(string[] args, string name)
        => ReadOption(args, name) is { Length: > 0 } value
            ? value
            : throw new UpgradeCommandException(2, $"缺少参数 {name}");

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index + 1 < args.Length; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
    }

    private static void ValidateReportPath(string? reportPath, string sourcePath, string command, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
            return;
        if (!Path.IsPathFullyQualified(reportPath))
            throw new UpgradeCommandException(2, "--report 必须是绝对路径");
        var report = Path.GetFullPath(reportPath);
        var source = Path.GetFullPath(sourcePath);
        var sourceIsDirectory = command.EndsWith("config", StringComparison.OrdinalIgnoreCase);
        if (PathsOverlap(source, report, sourceIsDirectory))
            throw new UpgradeCommandException(2, "--report 不能写入配置來源目录或覆盖来源数据库");
        if (!string.IsNullOrWhiteSpace(outputPath) && Path.IsPathFullyQualified(outputPath))
        {
            var output = Path.GetFullPath(outputPath);
            var outputIsDirectory = command.EndsWith("config", StringComparison.OrdinalIgnoreCase);
            if (PathsOverlap(output, report, outputIsDirectory))
                throw new UpgradeCommandException(2, "--report 不能写入转换输出目录或覆盖输出数据库");
        }
    }

    private static bool PathsOverlap(string left, string right, bool leftIsDirectory)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return true;
        return leftIsDirectory && right.StartsWith(left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class UpgradeCommandException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
