using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Reports;

/// <summary>
/// 从终态记录、原始输入 JSON 和不可变模板映射构造一次隔离的计算请求。
/// </summary>
public sealed class FileReportCalculationRequestFactory : IReportCalculationRequestFactory
{
    private readonly IRecordRepository _records;
    private readonly IModelResourceStore _resources;
    private readonly string _databaseInstanceId;
    private readonly string _workRoot;
    private readonly TimeSpan _timeout;

    public FileReportCalculationRequestFactory(
        IRecordRepository records,
        IModelResourceStore resources,
        string databaseInstanceId,
        string workRoot,
        TimeSpan timeout)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        if (!Guid.TryParseExact(databaseInstanceId, "D", out var databaseId)
            || !string.Equals(databaseInstanceId, databaseId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("数据库身份必须是小写 GUID D 格式。", nameof(databaseInstanceId));
        if (string.IsNullOrWhiteSpace(workRoot) || !Path.IsPathFullyQualified(workRoot))
            throw new ArgumentException("报表工作目录必须是绝对路径。", nameof(workRoot));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "报表计算期限必须大于零。");

        _databaseInstanceId = databaseInstanceId;
        _workRoot = Path.GetFullPath(workRoot);
        _timeout = timeout;
    }

    /// <summary>
    /// 校验冻结数据及资源版本，复制到本次尝试目录，再按白名单映射填写 XLSX。
    /// </summary>
    public async Task<ReportCalculationRequest> CreateAsync(
        ReportRecord report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!Guid.TryParseExact(report.RevisionId, "D", out var revisionId)
            || !string.Equals(report.RevisionId, revisionId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal)
            || !Guid.TryParseExact(report.AttemptToken, "D", out var attemptId)
            || !string.Equals(report.AttemptToken, attemptId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal)
            || report.Purpose is null
            || !IsHash(report.InputHash)
            || !IsHash(report.TemplateHash)
            || !IsHash(report.MappingHash))
            throw new InvalidDataException("报表任务缺少规范的版本、尝试令牌、用途或资源摘要。");

        var record = await _records.GetRecordAsync(report.TestRecordId, cancellationToken)
            ?? throw new InvalidDataException("报表任务关联的试验记录不存在。");
        if (string.IsNullOrWhiteSpace(record.ReportInputJson)
            || !string.Equals(record.InputHash, report.InputHash, StringComparison.Ordinal)
            || !string.Equals(ReportInputSnapshotSerializer.ComputeHash(record.ReportInputJson),
                report.InputHash, StringComparison.Ordinal))
            throw new InvalidDataException("试验记录缺少与报表任务一致的冻结输入。");

        var snapshot = ReportInputSnapshotSerializer.Deserialize(record.ReportInputJson);
        if (!string.Equals(snapshot.DatabaseInstanceId, _databaseInstanceId, StringComparison.Ordinal)
            || !string.Equals(snapshot.RunId, record.RunId, StringComparison.Ordinal)
            || !string.Equals(snapshot.RunId, report.ActiveAttemptKey, StringComparison.Ordinal)
            || !string.Equals(report.TemplatePath, snapshot.Resources.TemplateKey, StringComparison.Ordinal)
            || !string.Equals(snapshot.Resources.TemplateHash, report.TemplateHash, StringComparison.Ordinal)
            || !string.Equals(snapshot.Resources.MappingHash, report.MappingHash, StringComparison.Ordinal)
            || snapshot.Purpose != report.Purpose
            || snapshot.ExecutionState != record.State
            || record.ResultContractVersion != snapshot.ResultContractVersion)
            throw new InvalidDataException("报表任务、试验终态和冻结资源身份不匹配。");

        var attemptDirectory = Path.Combine(_workRoot, revisionId.ToString("N"), attemptId.ToString("N"));
        if (Directory.Exists(attemptDirectory))
            throw new IOException("本次报表尝试目录已存在，拒绝复用旧计算文件。");
        Directory.CreateDirectory(attemptDirectory);

        try
        {
            var templatePath = Path.Combine(attemptDirectory, "template.xlsx");
            var mappingPath = Path.Combine(attemptDirectory, "mapping.json");
            var inputJsonPath = Path.Combine(attemptDirectory, "input.json");
            var inputWorkbookPath = Path.Combine(attemptDirectory, "input.xlsx");

            await CopyResourceAsync(snapshot.Resources.TemplateKey, report.TemplateHash!, templatePath,
                cancellationToken);
            await CopyResourceAsync(snapshot.Resources.MappingKey, report.MappingHash!, mappingPath,
                cancellationToken);
            var mappingBytes = await File.ReadAllBytesAsync(mappingPath, cancellationToken);
            var templateDefinition = ReportTemplateMappingV1.Deserialize(mappingBytes,
                report.TemplateHash!, report.MappingHash!);
            if (templateDefinition.Purpose != snapshot.Purpose
                || !templateDefinition.SupportedRunScopes.Contains(snapshot.Scope))
                throw new InvalidDataException("冻结模板映射不支持本次报表用途或执行范围。");

            var inputJsonBytes = new UTF8Encoding(false, true).GetBytes(record.ReportInputJson);
            await File.WriteAllBytesAsync(inputJsonPath, inputJsonBytes, cancellationToken);
            File.Copy(templatePath, inputWorkbookPath, overwrite: false);
            await FillWorkbookAsync(inputWorkbookPath, snapshot, templateDefinition, cancellationToken);

            return new ReportCalculationRequest(
                revisionId,
                _databaseInstanceId,
                report.InputHash!,
                report.AttemptToken!,
                attemptDirectory,
                inputJsonPath,
                inputWorkbookPath,
                templatePath,
                mappingPath,
                templateDefinition,
                GeneratePdf: false,
                Timeout: _timeout);
        }
        catch
        {
            TryDeleteDirectory(attemptDirectory);
            throw;
        }
    }

    private async Task CopyResourceAsync(
        string resourceKey,
        string expectedHash,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = await _resources.OpenReadAsync(resourceKey, expectedHash, cancellationToken);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, count);
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException($"型号资源摘要不匹配：{resourceKey}");
    }

    private static async Task FillWorkbookAsync(
        string workbookPath,
        ReportInputSnapshotV1 snapshot,
        ReportTemplateDefinition definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(snapshot.TargetTimeZoneId);
        using var workbook = new XLWorkbook(workbookPath);

        foreach (var input in definition.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cell = ResolveTarget(workbook, input.Target);
            if (!string.IsNullOrEmpty(cell.FormulaA1))
                throw new InvalidDataException($"输入映射不能覆盖公式：{DescribeTarget(input.Target)}");

            var source = ResolveSource(snapshot, input.Source);
            if (!source.Found)
            {
                if (input.Required)
                    throw new InvalidDataException($"缺少必需报表输入：{input.SourceKey}");
                cell.Clear(XLClearOptions.Contents);
                continue;
            }

            if (source.ValueKind != input.ValueKind)
                throw new InvalidDataException($"报表输入值类型不匹配：{input.SourceKey}");
            if (!string.Equals(source.Unit, input.Unit, StringComparison.Ordinal))
                throw new InvalidDataException($"报表输入单位不匹配：{input.SourceKey}，来源 {source.Unit ?? "(无)"}，映射 {input.Unit ?? "(无)"}");

            switch (source.ValueKind)
            {
                case ReportValueKind.Text:
                    cell.Value = (string)source.Value!;
                    break;
                case ReportValueKind.Number:
                    cell.Value = (double)source.Value!;
                    break;
                case ReportValueKind.Boolean:
                    cell.Value = (bool)source.Value!;
                    break;
                case ReportValueKind.ExcelDate:
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc((DateTime)source.Value!, timeZone);
                    cell.Value = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
                    break;
                default:
                    throw new InvalidDataException($"未知报表值类型：{source.ValueKind}");
            }
        }

        if (snapshot.Purpose == ReportPurpose.ProductEvaluation)
        {
            var conclusion = definition.ProductConclusion
                ?? throw new InvalidDataException("产品判定模板缺少总判定公式目标。");
            foreach (var target in (conclusion.FormulaTargets ?? Array.Empty<ReportTarget>()).Append(conclusion.Target))
            {
                var formulaCell = ResolveTarget(workbook, target);
                if (string.IsNullOrWhiteSpace(formulaCell.FormulaA1))
                    throw new InvalidDataException($"产品判定映射目标没有 Excel 公式：{DescribeTarget(target)}");
            }
        }
        else
        {
            EnsureProcessOnlyInputs(definition);
        }

        workbook.Save();
        await Task.CompletedTask;
    }

    private static void EnsureProcessOnlyInputs(ReportTemplateDefinition definition)
    {
        if (definition.ProductConclusion is not null)
            throw new InvalidDataException("过程报告映射不能声明产品级总判定目标。");

        var requiredSystemFields = new[]
        {
            SystemReportField.EvaluationAllowed,
            SystemReportField.ExecutionMessage,
            SystemReportField.ReportPurpose
        };
        foreach (var field in requiredSystemFields)
        {
            if (!definition.Inputs.Any(input => input.Required
                && input.Source is SystemReportSource systemSource
                && systemSource.Field == field))
                throw new InvalidDataException($"过程报告映射必须声明必需系统字段：{field}");
        }
    }

    private static SourceValue ResolveSource(ReportInputSnapshotV1 snapshot, ReportInputSource source)
        => source switch
        {
            MetadataReportSource metadata => ResolveMetadata(snapshot.Metadata, metadata.Field),
            ParameterReportSource parameter => ResolveParameter(snapshot.Parameters, parameter.Field),
            MeasurementReportSource measurement => ResolveMeasurement(snapshot.Items, measurement),
            SystemReportSource system => ResolveSystem(snapshot, system.Field),
            _ => throw new InvalidDataException("映射包含不支持的数据源类型。")
        };

    private static SourceValue ResolveMetadata(ReportInputMetadata metadata, MetadataReportField field)
        => field switch
        {
            MetadataReportField.RecordNumber => Text(metadata.RecordNumber),
            MetadataReportField.RunId => Text(metadata.RunId),
            MetadataReportField.ProductNumber => OptionalText(metadata.ProductNumber),
            MetadataReportField.VehicleNumber => OptionalText(metadata.VehicleNumber),
            MetadataReportField.BatchNumber => OptionalText(metadata.BatchNumber),
            MetadataReportField.StationNumber => OptionalText(metadata.StationNumber),
            MetadataReportField.Remark => OptionalText(metadata.Remark),
            MetadataReportField.ProductModelName => Text(metadata.ProductModelName),
            MetadataReportField.OperatorName => Text(metadata.OperatorName),
            MetadataReportField.StartedAtUtc => Date(metadata.StartedAtUtc),
            MetadataReportField.FinishedAtUtc => metadata.FinishedAtUtc is { } value ? Date(value) : Missing(),
            MetadataReportField.DeviceMode => Text(metadata.DeviceMode.ToString()),
            MetadataReportField.RunScope => Text(metadata.Scope.ToString()),
            MetadataReportField.ExecutionState => Text(metadata.ExecutionState.ToString()),
            _ => throw new InvalidDataException($"未知元数据字段：{field}")
        };

    private static SourceValue ResolveParameter(
        IReadOnlyList<ReportInputParameter> parameters,
        FixedParameterReportField field)
    {
        var parameter = parameters.SingleOrDefault(item =>
            string.Equals(item.Field, field.ToString(), StringComparison.Ordinal));
        if (parameter is null)
            return Missing();
        if (parameter.Kind != MeasuredValueKind.Number || parameter.Number is null)
            throw new InvalidDataException($"固定参数 {field} 必须是数值及单位。");
        return Number(parameter.Number.Value, parameter.Unit);
    }

    private static SourceValue ResolveMeasurement(
        IReadOnlyList<ReportInputTestItem> items,
        MeasurementReportSource source)
    {
        var item = items.SingleOrDefault(candidate => candidate.PointId == source.PointId);
        var measurement = item?.Measurements.SingleOrDefault(candidate =>
            string.Equals(candidate.Key, source.Key, StringComparison.Ordinal));
        if (measurement is null || !measurement.Valid)
            return Missing();

        return measurement.Kind switch
        {
            MeasuredValueKind.Number when measurement.Number is { } number => Number(number, measurement.Unit),
            MeasuredValueKind.Text when measurement.Text is { } text => Text(text, measurement.Unit),
            MeasuredValueKind.Boolean when measurement.Boolean is { } boolean => Boolean(boolean, measurement.Unit),
            _ => throw new InvalidDataException($"测量 {source.PointId}/{source.Key} 的数据与类型不一致。")
        };
    }

    private static SourceValue ResolveSystem(ReportInputSnapshotV1 snapshot, SystemReportField field)
        => field switch
        {
            SystemReportField.EvaluationAllowed => Boolean(
                snapshot.Purpose == ReportPurpose.ProductEvaluation
                && snapshot.ExecutionState == RecordState.Completed
                && snapshot.Items.All(item => item.State == ItemResultState.Succeeded)),
            SystemReportField.ExecutionMessage => Text(snapshot.ExecutionError
                ?? (snapshot.Purpose == ReportPurpose.ProcessOnly ? "未作完整判定" : string.Empty)),
            SystemReportField.ReportPurpose => Text(snapshot.Purpose.ToString()),
            _ => throw new InvalidDataException($"未知报表系统字段：{field}")
        };

    private static IXLCell ResolveTarget(XLWorkbook workbook, ReportTarget target)
    {
        target.EnsureValid();
        if (!string.IsNullOrWhiteSpace(target.CellAddress))
        {
            var worksheet = workbook.Worksheets.FirstOrDefault(sheet =>
                string.Equals(sheet.Name, target.WorksheetName, StringComparison.OrdinalIgnoreCase));
            return worksheet?.Cell(target.CellAddress)
                ?? throw new InvalidDataException($"报表目标工作表不存在：{target.WorksheetName}");
        }

        var names = string.IsNullOrWhiteSpace(target.WorksheetName)
            ? workbook.DefinedNames
            : workbook.Worksheet(target.WorksheetName).DefinedNames;
        var ranges = names.DefinedName(target.DefinedName!).Ranges.ToList();
        if (ranges.Count != 1)
            throw new InvalidDataException($"报表命名区域必须只引用一个区域：{target.DefinedName}");
        var cells = ranges[0].Cells().ToList();
        if (cells.Count != 1)
            throw new InvalidDataException($"报表命名区域必须只引用一个单元格：{target.DefinedName}");
        return cells[0];
    }

    private static SourceValue Text(string value, string? unit = null)
        => new(true, value, ReportValueKind.Text, unit);
    private static SourceValue OptionalText(string? value)
        => value is null ? Missing() : Text(value);
    private static SourceValue Number(double value, string? unit = null)
        => double.IsFinite(value)
            ? new(true, value, ReportValueKind.Number, unit)
            : throw new InvalidDataException("报表数值必须是有限数。");
    private static SourceValue Boolean(bool value, string? unit = null)
        => new(true, value, ReportValueKind.Boolean, unit);
    private static SourceValue Date(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? new(true, value, ReportValueKind.ExcelDate, null)
            : throw new InvalidDataException("报表时间必须是 UTC。");
    private static SourceValue Missing() => new(false, null, ReportValueKind.Text, null);

    private static string DescribeTarget(ReportTarget target)
        => !string.IsNullOrWhiteSpace(target.DefinedName)
            ? $"名称 {target.DefinedName}"
            : $"{target.WorksheetName}!{target.CellAddress}";

    private static bool IsHash(string? value)
        => value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // 清理临时文件失败不覆盖报表处理错误。
        }
        catch (UnauthorizedAccessException)
        {
            // 清理临时文件失败不覆盖报表处理错误。
        }
    }

    private sealed record SourceValue(bool Found, object? Value, ReportValueKind ValueKind, string? Unit);
}
