using XXX.TestBench.Core.Common;
using System.Security.Cryptography;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Execution;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>按型号导入、验证和解析不可变 Excel 模板、映射及准备指导资源。</summary>
public sealed class ModelResourceService
{
    private readonly IProductRepository _products;
    private readonly IModelResourceProfileRepository _profiles;
    private readonly IModelResourceStore _resources;
    private readonly IUnitOfWorkFactory _transactions;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public ModelResourceService(IProductRepository products, IModelResourceProfileRepository profiles,
        IModelResourceStore resources, IUnitOfWorkFactory transactions, IAuditLog audit, IClock clock)
    {
        _products = products;
        _profiles = profiles;
        _resources = resources;
        _transactions = transactions;
        _audit = audit;
        _clock = clock;
    }

    public Task<ModelResourceProfile?> GetProfileAsync(int productModelId, CancellationToken cancellationToken = default)
        => _profiles.GetAsync(productModelId, cancellationToken);

    /// <summary>读取并复核型号当前绑定的图文指导包；未配置时返回 null。</summary>
    public async Task<ModelPreparationGuidanceSnapshot?> GetPreparationGuidanceAsync(
        int productModelId, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(productModelId, cancellationToken);
        if (profile?.GuidanceKey is not { } resourceKey)
            return null;

        var hash = await _resources.ResolveUniqueHashAsync(resourceKey, cancellationToken);
        await using var source = await _resources.OpenReadAsync(resourceKey, hash, cancellationToken);
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (content.Length + count > PreparationGuidancePackage.MaxPackageBytes)
                throw new InvalidDataException("准备指导 ZIP 包超过 32 MB。");
            await content.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        var package = await PreparationGuidancePackage.ReadValidatedAsync(content.ToArray(), cancellationToken);
        return new ModelPreparationGuidanceSnapshot(
            productModelId, resourceKey, hash, package.Content, package.Images);
    }

    /// <summary>
    /// 导入模板和映射为同一不可变版本，再以型号行版本条件发布配置。
    /// ProductEvaluation 必须有 Excel 总判定；ProcessOnly 禁止声明产品结论。
    /// </summary>
    public async Task<ModelResourceProfile> ImportReportPairAsync(int productModelId, ReportPurpose purpose,
        string templatePath, string mappingPath, UserContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.EnsurePermission(PermissionCode.ManageProducts);
        if (purpose is not (ReportPurpose.ProductEvaluation or ReportPurpose.ProcessOnly))
            throw new DomainException("报表资源用途无效。");
        var model = await _products.GetModelAsync(productModelId, cancellationToken)
            ?? throw new DomainException("所选产品型号不存在。");
        ValidateFile(templatePath, ".xlsx", "Excel 模板");
        ValidateFile(mappingPath, ".json", "报表映射");

        var profileBeforeImport = await _profiles.GetAsync(productModelId, cancellationToken);
        var versionKey = Guid.NewGuid().ToString("N");
        var prefix = $"models/{productModelId}/reports/{purpose}/{versionKey}";
        var templateKey = prefix + "/template.xlsx";
        var mappingKey = prefix + "/mapping.json";

        var mappingBytes = await File.ReadAllBytesAsync(mappingPath, cancellationToken);
        var mappingHash = Convert.ToHexString(SHA256.HashData(mappingBytes)).ToLowerInvariant();
        string templateHash;
        await using (var template = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            templateHash = Convert.ToHexString(await SHA256.HashDataAsync(template, cancellationToken)).ToLowerInvariant();
        var definition = ReportTemplateMappingV1.Deserialize(mappingBytes, templateHash, mappingHash);
        ValidatePurposeAndScope(definition, purpose);

        // 先完成映射合同校验，再导入任何内容，避免错误映射留下不可引用的资源版本。
        await using (var template = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var importedHash = await _resources.ImportAsync(templateKey, template, cancellationToken);
            if (!string.Equals(importedHash, templateHash, StringComparison.Ordinal))
                throw new IOException("Excel 模板在导入前发生变化，请重新选择文件。");
        }
        await using (var mappingStream = new MemoryStream(mappingBytes, writable: false))
        {
            var importedHash = await _resources.ImportAsync(mappingKey, mappingStream, cancellationToken);
            if (!string.Equals(importedHash, mappingHash, StringComparison.Ordinal))
                throw new IOException("报表映射在导入过程中发生变化，请重新选择文件。");
        }

        var current = profileBeforeImport;
        var updated = new ModelResourceProfile
        {
            ProductModelId = productModelId,
            TemplateKey = purpose == ReportPurpose.ProductEvaluation ? templateKey : current?.TemplateKey
                ?? throw new DomainException("首次配置时必须先导入产品判定模板和映射。"),
            MappingKey = purpose == ReportPurpose.ProductEvaluation ? mappingKey : current!.MappingKey,
            ProcessTemplateKey = purpose == ReportPurpose.ProcessOnly ? templateKey : current?.ProcessTemplateKey,
            ProcessMappingKey = purpose == ReportPurpose.ProcessOnly ? mappingKey : current?.ProcessMappingKey,
            GuidanceKey = current?.GuidanceKey,
            Revision = checked((current?.Revision ?? 0) + 1),
            UpdatedBy = actor.LoginName,
            UpdatedAtUtc = _clock.UtcNow,
            RowVersion = current?.RowVersion ?? 0
        };
        updated.EnsureValid();

        await using var unitOfWork = _transactions.Create();
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _profiles.SaveAsync(updated, current?.RowVersion, cancellationToken);
            await _audit.WriteAsync(actor.LoginName, "ModelReportResourcesImported", model.Name,
                $"Purpose={purpose}; Revision={updated.Revision}; TemplateHash={templateHash}; MappingHash={mappingHash}",
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return updated;
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>校验图文指导 ZIP 包并作为新不可变版本绑定到型号。</summary>
    public async Task<ModelResourceProfile> ImportGuidancePackageAsync(int productModelId, string packagePath,
        UserContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.EnsurePermission(PermissionCode.ManageProducts);
        ValidateFile(packagePath, ".zip", "准备指导 ZIP 包");
        var model = await _products.GetModelAsync(productModelId, cancellationToken)
            ?? throw new DomainException("所选产品型号不存在。");
        var current = await _profiles.GetAsync(productModelId, cancellationToken)
            ?? throw new DomainException("请先配置该型号的产品判定模板和映射。");

        var fileInfo = new FileInfo(packagePath);
        if (fileInfo.Length > PreparationGuidancePackage.MaxPackageBytes)
            throw new InvalidDataException("准备指导 ZIP 包超过 32 MB。");
        var packageBytes = await File.ReadAllBytesAsync(packagePath, cancellationToken);
        await PreparationGuidancePackage.ValidateAsync(packageBytes, cancellationToken);

        var key = $"models/{productModelId}/guidance/{Guid.NewGuid():N}/package.zip";
        var expectedHash = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        await using (var package = new MemoryStream(packageBytes, writable: false))
        {
            var importedHash = await _resources.ImportAsync(key, package, cancellationToken);
            if (!string.Equals(importedHash, expectedHash, StringComparison.Ordinal))
                throw new IOException("准备指导包在导入过程中发生变化，请重新选择文件。");
        }

        var updated = current with
        {
            GuidanceKey = key,
            Revision = checked(current.Revision + 1),
            UpdatedBy = actor.LoginName,
            UpdatedAtUtc = _clock.UtcNow
        };
        updated.EnsureValid();

        await using var unitOfWork = _transactions.Create();
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _profiles.SaveAsync(updated, current.RowVersion, cancellationToken);
            await _audit.WriteAsync(actor.LoginName, "ModelPreparationGuidanceImported", model.Name,
                $"Revision={updated.Revision}; GuidanceHash={expectedHash}", cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return updated;
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>返回冻结试验输入所需的资源键和摘要；每个键必须只指向一个版本。</summary>
    public async Task<ReportInputResources> GetReportResourcesAsync(int productModelId, ReportPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(productModelId, cancellationToken)
            ?? throw new DomainException("该产品型号尚未配置报表资源。");
        var (templateKey, mappingKey) = purpose switch
        {
            ReportPurpose.ProductEvaluation => (profile.TemplateKey, profile.MappingKey),
            ReportPurpose.ProcessOnly when profile.ProcessTemplateKey is not null && profile.ProcessMappingKey is not null
                => (profile.ProcessTemplateKey, profile.ProcessMappingKey),
            ReportPurpose.ProcessOnly => throw new DomainException("该产品型号尚未配置过程报告模板和映射。"),
            _ => throw new DomainException("报表用途无效。")
        };
        var templateHash = await _resources.ResolveUniqueHashAsync(templateKey, cancellationToken);
        var mappingHash = await _resources.ResolveUniqueHashAsync(mappingKey, cancellationToken);
        await using (var mapping = await _resources.OpenReadAsync(mappingKey, mappingHash, cancellationToken))
        using (var buffer = new MemoryStream())
        {
            await mapping.CopyToAsync(buffer, cancellationToken);
            var definition = ReportTemplateMappingV1.Deserialize(buffer.ToArray(), templateHash, mappingHash);
            ValidatePurposeAndScope(definition, purpose);
        }
        var guidanceHash = profile.GuidanceKey is null
            ? null
            : await _resources.ResolveUniqueHashAsync(profile.GuidanceKey, cancellationToken);
        if (profile.GuidanceKey is not null && guidanceHash is not null)
        {
            await using var package = await _resources.OpenReadAsync(profile.GuidanceKey, guidanceHash, cancellationToken);
            using var content = new MemoryStream();
            await package.CopyToAsync(content, cancellationToken);
            await PreparationGuidancePackage.ValidateAsync(content.ToArray(), cancellationToken);
        }
        return new ReportInputResources
        {
            TemplateKey = templateKey,
            TemplateHash = templateHash,
            MappingKey = mappingKey,
            MappingHash = mappingHash,
            GuidanceKey = profile.GuidanceKey,
            GuidanceHash = guidanceHash
        };
    }

    private static void ValidatePurposeAndScope(ReportTemplateDefinition definition, ReportPurpose purpose)
    {
        if (definition.Purpose != purpose)
            throw new InvalidDataException($"映射声明用途为 {definition.Purpose}，与导入类型 {purpose} 不一致。");
        if (purpose == ReportPurpose.ProductEvaluation
            && (!definition.SupportedRunScopes.Contains(RunScope.Auto) || definition.ProductConclusion is null))
            throw new InvalidDataException("产品判定映射必须支持自动试验，并明确 Excel 总判定目标和合格/不合格文本。");
        if (purpose == ReportPurpose.ProcessOnly
            && (definition.ProductConclusion is not null
                || !definition.SupportedRunScopes.Contains(RunScope.Auto)
                || !definition.SupportedRunScopes.Contains(RunScope.Single)))
            throw new InvalidDataException("过程报告映射不得包含产品结论，并须支持自动和单项执行范围。");
    }

    private static void ValidateFile(string path, string extension, string displayName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new FileNotFoundException($"{displayName}路径无效或文件不存在。", path);
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{displayName}必须使用 {extension} 格式。");
    }
}
