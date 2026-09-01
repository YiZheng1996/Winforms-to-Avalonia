using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Domain.Recipes;

/// <summary>某产品型号的一版配方。Published 一经使用不可原地修改。</summary>
public sealed class RecipeVersion
{
    public int Id { get; set; }
    public int ProductModelId { get; init; }
    public int Version { get; init; }
    public RecipeStatus Status { get; internal set; } = RecipeStatus.Draft;
    public required string Name { get; set; }
    public string? ReportTemplatePath { get; set; }
    public int CreatedByUserId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int? PublishedByUserId { get; internal set; }
    public DateTime? PublishedAtUtc { get; internal set; }
    public int? RetiredByUserId { get; internal set; }
    public DateTime? RetiredAtUtc { get; internal set; }

    public void Publish(int userId, DateTime utcNow)
    {
        if (Status != RecipeStatus.Draft) throw new DomainException("只有草稿配方可以发布");
        Status = RecipeStatus.Published;
        PublishedByUserId = userId;
        PublishedAtUtc = utcNow;
    }

    public void Retire(int userId, DateTime utcNow)
    {
        if (Status != RecipeStatus.Published) throw new DomainException("只有已发布配方可以停用");
        Status = RecipeStatus.Retired;
        RetiredByUserId = userId;
        RetiredAtUtc = utcNow;
    }

    /// <summary>复制为新草稿版本（Published 修改必须复制为新版本）。</summary>
    public RecipeVersion CreateNewVersion(int newVersion, int userId, DateTime utcNow) => new()
    {
        ProductModelId = ProductModelId,
        Version = newVersion,
        Name = Name,
        ReportTemplatePath = ReportTemplatePath,
        CreatedByUserId = userId,
        CreatedAtUtc = utcNow
    };
}
