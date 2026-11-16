using System.Text.Json.Serialization;
using XXX.TestBench.Core.Domain.Records;

namespace XXX.TestBench.Core.Domain.Reports;

/// <summary>
/// 报表映射允许读取的记录元数据字段。
/// </summary>
public enum MetadataReportField
{
    RecordNumber = 0,
    RunId = 1,
    ProductNumber = 2,
    VehicleNumber = 3,
    BatchNumber = 4,
    StationNumber = 5,
    Remark = 6,
    ProductModelName = 7,
    OperatorName = 8,
    StartedAtUtc = 9,
    FinishedAtUtc = 10,
    DeviceMode = 11,
    RunScope = 12,
    ExecutionState = 13
}

/// <summary>
/// 报表映射允许读取的固定参数字段。
/// </summary>
public enum FixedParameterReportField
{
    TestTimeSeconds = 0,
    TestVoltageV = 1,
    ProtectCurrentMa = 2
}

/// <summary>
/// 报表用例提供的受控系统字段。
/// </summary>
public enum SystemReportField
{
    EvaluationAllowed = 0,
    ExecutionMessage = 1,
    ReportPurpose = 2
}

public enum ReportValueKind
{
    Text = 0,
    Number = 1,
    Boolean = 2,
    ExcelDate = 3
}

/// <summary>
/// 白名单报表数据源。不得扩展为反射属性、表达式、SQL 或任意路径读取。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MetadataReportSource), "metadata")]
[JsonDerivedType(typeof(ParameterReportSource), "parameter")]
[JsonDerivedType(typeof(MeasurementReportSource), "measurement")]
[JsonDerivedType(typeof(SystemReportSource), "system")]
public abstract record ReportInputSource
{
    public abstract string GetSourceKey();
}

public sealed record MetadataReportSource(MetadataReportField Field) : ReportInputSource
{
    public override string GetSourceKey() => $"metadata:{(int)Field}";
}

public sealed record ParameterReportSource(FixedParameterReportField Field) : ReportInputSource
{
    public override string GetSourceKey() => $"parameter:{(int)Field}";
}

public sealed record MeasurementReportSource(int PointId, string Key) : ReportInputSource
{
    public override string GetSourceKey() => $"measurement:{PointId}:{Key.Length}:{Key}";
}

public sealed record SystemReportSource(SystemReportField Field) : ReportInputSource
{
    public override string GetSourceKey() => $"system:{(int)Field}";
}

/// <summary>
/// 只允许明确工作表单元格或命名区域作为目标。
/// </summary>
public sealed record ReportTarget
{
    public string? WorksheetName { get; init; }
    public string? CellAddress { get; init; }
    public string? DefinedName { get; init; }

    public void EnsureValid()
    {
        var hasCell = !string.IsNullOrWhiteSpace(CellAddress);
        var hasName = !string.IsNullOrWhiteSpace(DefinedName);
        if (hasCell == hasName)
            throw new InvalidDataException("报表目标必须且只能是单元格或命名区域");
        if (hasCell && string.IsNullOrWhiteSpace(WorksheetName))
            throw new InvalidDataException("单元格目标必须指定工作表");
        if (hasCell && CellAddress!.Any(char.IsWhiteSpace))
            throw new InvalidDataException("单元格目标不能包含空白字符");
    }

    internal string ToComparisonKey()
    {
        EnsureValid();
        if (!string.IsNullOrWhiteSpace(DefinedName))
            return $"name:{WorksheetName?.Trim().ToUpperInvariant()}:{DefinedName.Trim().ToUpperInvariant()}";
        var address = CellAddress!.Replace("$", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return $"cell:{WorksheetName!.Trim().ToUpperInvariant()}:{address}";
    }
}

public sealed record ReportInputDefinition(
    ReportInputSource Source,
    ReportTarget Target,
    ReportValueKind ValueKind,
    string? Unit,
    bool Required)
{
    public string SourceKey => Source.GetSourceKey();
}

/// <summary>
/// Excel 原结论文本到产品结论状态的精确映射；不使用包含关系判断。
/// </summary>
public sealed record ReportConclusionDefinition(
    ReportTarget Target,
    IReadOnlyList<string> QualifiedValues,
    IReadOnlyList<string> UnqualifiedValues,
    IReadOnlyList<ReportTarget> FormulaTargets);

/// <summary>
/// 一个已导入的不可变模板映射合同。单元格名、公式和实际结论值需从真实模板核验。
/// </summary>
public sealed record ReportTemplateDefinition
{
    public required string TemplateId { get; init; }
    public required string Version { get; init; }
    public required string TemplateHash { get; init; }
    public required string MappingHash { get; init; }
    public ReportPurpose Purpose { get; init; }
    public required IReadOnlyList<RunScope> SupportedRunScopes { get; init; }
    public required IReadOnlyList<ReportInputDefinition> Inputs { get; init; }
    public ReportConclusionDefinition? ProductConclusion { get; init; }

    /// <summary>
    /// 检查 DTO 层可确定的合同；工作簿单元格、公式和宏/外链仍由导入适配器读取真实模板验证。
    /// </summary>
    public IReadOnlyList<string> ValidateContract()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(TemplateId)) errors.Add("TemplateId 不能为空");
        if (string.IsNullOrWhiteSpace(Version)) errors.Add("模板版本不能为空");
        ValidateHash(TemplateHash, "TemplateHash", errors);
        ValidateHash(MappingHash, "MappingHash", errors);
        if (!Enum.IsDefined(Purpose)) errors.Add("Purpose 未知");
        if (SupportedRunScopes is null || SupportedRunScopes.Count == 0)
            errors.Add("至少声明一个支持的执行范围");
        else if (SupportedRunScopes.Any(scope => !Enum.IsDefined(scope))
                 || SupportedRunScopes.Distinct().Count() != SupportedRunScopes.Count)
            errors.Add("执行范围包含未知值或重复值");
        if (Inputs is null) errors.Add("Inputs 不能为空");

        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Inputs is not null)
        {
            foreach (var input in Inputs)
            {
                if (input is null || input.Source is null || input.Target is null)
                {
                    errors.Add("输入映射缺少数据源或目标");
                    continue;
                }

                if (!Enum.IsDefined(input.ValueKind)) errors.Add("输入映射包含未知值类型");
                if (!IsKnownSource(input.Source)) errors.Add("输入映射包含未知或无效数据源");
                else if (!sourceKeys.Add(input.SourceKey)) errors.Add($"输入数据源重复：{input.SourceKey}");

                try
                {
                    var targetKey = input.Target.ToComparisonKey();
                    if (!targetKeys.Add(targetKey)) errors.Add($"输入目标重复：{targetKey}");
                }
                catch (InvalidDataException ex)
                {
                    errors.Add(ex.Message);
                }
            }
        }

        if (Purpose == ReportPurpose.ProductEvaluation && ProductConclusion is null)
            errors.Add("产品判定模板必须声明结论映射");
        if (ProductConclusion is not null)
        {
            try
            {
                var outputKey = ProductConclusion.Target.ToComparisonKey();
                if (!targetKeys.Add(outputKey)) errors.Add($"结论目标与输入映射重叠：{outputKey}");
            }
            catch (InvalidDataException ex)
            {
                errors.Add(ex.Message);
            }

            var qualified = ProductConclusion.QualifiedValues ?? Array.Empty<string>();
            var unqualified = ProductConclusion.UnqualifiedValues ?? Array.Empty<string>();
            if (qualified.Count == 0 || unqualified.Count == 0
                || qualified.Any(string.IsNullOrWhiteSpace) || unqualified.Any(string.IsNullOrWhiteSpace))
                errors.Add("合格和不合格结论必须各有明确文本值");
            if (qualified.Intersect(unqualified, StringComparer.Ordinal).Any())
                errors.Add("合格与不合格结论文本不能重叠");

            foreach (var formulaTarget in ProductConclusion.FormulaTargets ?? Array.Empty<ReportTarget>())
            {
                try { formulaTarget.ToComparisonKey(); }
                catch (InvalidDataException ex) { errors.Add(ex.Message); }
            }
        }

        return errors;
    }

    private static bool IsKnownSource(ReportInputSource source) => source switch
    {
        MetadataReportSource value => Enum.IsDefined(value.Field),
        ParameterReportSource value => Enum.IsDefined(value.Field),
        MeasurementReportSource value => value.PointId > 0 && !string.IsNullOrWhiteSpace(value.Key),
        SystemReportSource value => Enum.IsDefined(value.Field),
        _ => false
    };

    private static void ValidateHash(string value, string field, ICollection<string> errors)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character))
            || value.Any(char.IsUpper))
            errors.Add($"{field} 必须为 64 位小写 SHA-256");
    }
}
