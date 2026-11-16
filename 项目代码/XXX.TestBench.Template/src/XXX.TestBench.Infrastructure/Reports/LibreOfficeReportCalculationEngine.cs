using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Reports;

/// <summary>
/// 使用隔离的 LibreOffice 进程重算 XLSX，并读取模板声明的公式结论目标。
/// </summary>
public sealed class LibreOfficeReportCalculationEngine : IReportCalculationEngine
{
    private const string EngineName = "LibreOffice";
    private const string FormulaConclusionSource = "ExcelFormula";
    private const int MaxDiagnosticCharacters = 8192;

    private readonly string _executablePath;
    private readonly string _engineVersion;

    /// <summary>
    /// 创建 LibreOffice 报表计算引擎。
    /// </summary>
    public LibreOfficeReportCalculationEngine(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("报表工具路径必须是绝对路径。", nameof(executablePath));

        _executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(_executablePath))
            throw new FileNotFoundException("LibreOffice 可执行文件不存在。", _executablePath);

        var version = FileVersionInfo.GetVersionInfo(_executablePath).ProductVersion;
        _engineVersion = string.IsNullOrWhiteSpace(version) ? "unknown" : version;
    }

    /// <summary>
    /// 使用独立用户配置目录转换并重算输入 XLSX。
    /// </summary>
    public async Task<ReportCalculationResult> CalculateAsync(
        ReportCalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var inputPath = ValidateRequest(request);
        var inputDirectory = Path.GetDirectoryName(inputPath)!;
        var workDirectory = Path.Combine(inputDirectory,
            $".report-engine-{request.RevisionId:N}-{Guid.NewGuid():N}");
        var outputDirectory = Path.Combine(workDirectory, "output");
        var profileDirectory = Path.Combine(workDirectory, "profile");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(profileDirectory);

        var calculationInputPath = Path.Combine(workDirectory, Path.GetFileName(inputPath));
        using (var inputWorkbook = new XLWorkbook(inputPath))
        {
            // 显式恢复自动计算，避免模板原有的手动计算模式沿用旧缓存值。
            inputWorkbook.CalculateMode = XLCalculateMode.Auto;
            inputWorkbook.SaveAs(calculationInputPath);
        }

        var outputPath = Path.Combine(outputDirectory, Path.GetFileName(inputPath));
        var startInfo = CreateStartInfo(_executablePath, calculationInputPath, outputDirectory, profileDirectory, workDirectory);
        using var process = new Process { StartInfo = startInfo };
        var succeeded = false;

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("LibreOffice 计算进程未能启动。");

            var standardOutputTask = ReadLimitedAsync(process.StandardOutput);
            var standardErrorTask = ReadLimitedAsync(process.StandardError);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Timeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                await WaitForExitAfterKillAsync(process);
                await Task.WhenAll(standardOutputTask, standardErrorTask);

                if (cancellationToken.IsCancellationRequested)
                    throw;

                return Failure(request, "Timeout", $"LibreOffice 超过 {request.Timeout} 未完成报表计算。");
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            if (process.ExitCode != 0)
                return Failure(request, "LibreOfficeExit",
                    BuildProcessError(process.ExitCode, standardOutput, standardError));
            if (!File.Exists(outputPath))
                return Failure(request, "OutputMissing", "LibreOffice 未生成预期的 XLSX 文件。");
            if (string.Equals(Path.GetFullPath(outputPath), inputPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return Failure(request, "OutputValidation", "计算输出不能覆盖冻结输入工作簿。");

            var conclusion = ReadConclusion(request, outputPath);
            var outputHash = await ComputeSha256Async(outputPath, cancellationToken);
            var result = new ReportCalculationResult(
                true,
                request.RevisionId,
                request.InputHash,
                EngineName,
                _engineVersion,
                outputPath,
                outputHash,
                null,
                null,
                conclusion.Source,
                conclusion.Text,
                null,
                null);
            succeeded = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch (Win32Exception ex)
        {
            return Failure(request, "ProcessStart", ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return Failure(request, "FormulaValidation", ex.Message);
        }
        catch (IOException ex)
        {
            return Failure(request, "FileIO", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure(request, "FileAccess", ex.Message);
        }
        finally
        {
            TryDeleteDirectory(profileDirectory);
            if (!succeeded)
                TryDeleteDirectory(workDirectory);
        }
    }

    private static string ValidateRequest(ReportCalculationRequest request)
    {
        if (request.RevisionId == Guid.Empty || !IsSha256(request.InputHash)
            || !Guid.TryParseExact(request.AttemptToken, "D", out var attemptId)
            || !string.Equals(request.AttemptToken, attemptId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal)
            || request.Timeout <= TimeSpan.Zero
            || request.GeneratePdf
            || string.IsNullOrWhiteSpace(request.WorkDirectoryPath)
            || !Path.IsPathFullyQualified(request.WorkDirectoryPath)
            || string.IsNullOrWhiteSpace(request.InputWorkbookPath)
            || !Path.IsPathFullyQualified(request.InputWorkbookPath))
            throw new InvalidDataException("LibreOffice 请求的版本、摘要、期限或 XLSX 输入无效。");

        var workDirectory = Path.GetFullPath(request.WorkDirectoryPath);
        if (!Directory.Exists(workDirectory)
            || new[] { request.InputJsonPath, request.InputWorkbookPath, request.TemplatePath, request.MappingPath }
                .Any(path => !IsWithinDirectory(workDirectory, path)))
            throw new InvalidDataException("LibreOffice 请求文件必须位于现存的任务工作目录内。");

        var inputPath = Path.GetFullPath(request.InputWorkbookPath);
        if (!string.Equals(Path.GetExtension(inputPath), ".xlsx", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(inputPath))
            throw new FileNotFoundException("公式计算只接受存在的 XLSX 输入工作簿。", inputPath);

        var contractErrors = request.TemplateDefinition.ValidateContract();
        if (contractErrors.Count > 0)
            throw new InvalidDataException("报表模板映射合同无效：" + string.Join("；", contractErrors));

        if (request.TemplateDefinition.Purpose == ReportPurpose.ProductEvaluation
            && request.TemplateDefinition.ProductConclusion is null)
            throw new InvalidDataException("产品判定模板没有结论目标定义。");

        return inputPath;
    }

    private static ProcessStartInfo CreateStartInfo(
        string executablePath,
        string inputPath,
        string outputDirectory,
        string profileDirectory,
        string workingDirectory)
    {
        var profileUri = new Uri(Path.GetFullPath(profileDirectory)).AbsoluteUri;
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--nodefault");
        startInfo.ArgumentList.Add("--norestore");
        startInfo.ArgumentList.Add("--nolockcheck");
        startInfo.ArgumentList.Add("-env:UserInstallation=" + profileUri);
        startInfo.ArgumentList.Add("--convert-to");
        startInfo.ArgumentList.Add("xlsx");
        startInfo.ArgumentList.Add("--outdir");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add(inputPath);
        return startInfo;
    }

    private static (string? Source, string? Text) ReadConclusion(
        ReportCalculationRequest request,
        string calculatedWorkbookPath)
    {
        if (request.TemplateDefinition.Purpose != ReportPurpose.ProductEvaluation)
            return (null, null);

        var definition = request.TemplateDefinition.ProductConclusion
            ?? throw new InvalidDataException("产品判定模板缺少结论映射。");
        using var workbook = new XLWorkbook(calculatedWorkbookPath);

        var targets = definition.FormulaTargets.ToList();
        targets.Add(definition.Target);

        foreach (var target in targets)
        {
            var cell = ResolveTarget(workbook, target);
            if (string.IsNullOrWhiteSpace(cell.FormulaA1))
                throw new InvalidDataException($"声明的公式目标没有公式：{DescribeTarget(target)}");
            if (cell.NeedsRecalculation)
                throw new InvalidDataException($"公式目标仍标记为需要重算：{DescribeTarget(target)}");

            var value = cell.CachedValue;
            if (value.IsError)
                throw new InvalidDataException($"公式目标返回 Excel 错误：{DescribeTarget(target)}={value.GetError()}");
            if (value.IsBlank || (value.IsText && string.IsNullOrWhiteSpace(value.GetText())))
                throw new InvalidDataException($"公式目标没有有效结果：{DescribeTarget(target)}");
        }

        var conclusionCell = ResolveTarget(workbook, definition.Target);
        var conclusionValue = conclusionCell.CachedValue;
        if (!conclusionValue.IsText)
            throw new InvalidDataException("总判定公式必须返回文本值。");

        return (FormulaConclusionSource, conclusionValue.GetText());
    }

    private static IXLCell ResolveTarget(XLWorkbook workbook, ReportTarget target)
    {
        target.EnsureValid();
        if (!string.IsNullOrWhiteSpace(target.CellAddress))
            return workbook.Worksheet(target.WorksheetName!).Cell(target.CellAddress);

        var names = string.IsNullOrWhiteSpace(target.WorksheetName)
            ? workbook.DefinedNames
            : workbook.Worksheet(target.WorksheetName).DefinedNames;
        var ranges = names.DefinedName(target.DefinedName!).Ranges.ToList();
        if (ranges.Count != 1)
            throw new InvalidDataException($"命名区域必须只引用一个区域：{target.DefinedName}");

        var cells = ranges[0].Cells().ToList();
        if (cells.Count != 1)
            throw new InvalidDataException($"结论命名区域必须只引用一个单元格：{target.DefinedName}");
        return cells[0];
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader)
    {
        var builder = new StringBuilder(Math.Min(MaxDiagnosticCharacters, 1024));
        var buffer = new char[1024];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            var remaining = MaxDiagnosticCharacters - builder.Length;
            if (remaining > 0)
                builder.Append(buffer, 0, Math.Min(count, remaining));
            if (count > remaining)
                truncated = true;
        }

        if (truncated)
            builder.Append(" …[诊断输出已截断]");
        return builder.ToString();
    }

    private static string BuildProcessError(int exitCode, string standardOutput, string standardError)
    {
        var diagnostic = string.Join("\n", new[] { standardError, standardOutput }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (diagnostic.Length > MaxDiagnosticCharacters)
            diagnostic = diagnostic[..MaxDiagnosticCharacters];
        return string.IsNullOrWhiteSpace(diagnostic)
            ? $"LibreOffice 退出码为 {exitCode}。"
            : $"LibreOffice 退出码为 {exitCode}：{diagnostic}";
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // 进程可能已在取消到达前退出。
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 进程已经退出。
        }
        catch (Win32Exception)
        {
            // 进程在取消竞态中无法再次终止。
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不应覆盖计算结果或原始失败原因。
        }
        catch (UnauthorizedAccessException)
        {
            // 清理失败不应覆盖计算结果或原始失败原因。
        }
    }

    private async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private ReportCalculationResult Failure(ReportCalculationRequest request, string stage, string message)
        => new(false, request.RevisionId, request.InputHash, EngineName, _engineVersion,
            null, null, null, null, null, null, stage, message);

    private static string DescribeTarget(ReportTarget target)
        => !string.IsNullOrWhiteSpace(target.DefinedName)
            ? $"名称 {target.DefinedName}"
            : $"{target.WorksheetName}!{target.CellAddress}";

    private static bool IsSha256(string? value)
        => value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsWithinDirectory(string directory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;
        var relative = Path.GetRelativePath(directory, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
