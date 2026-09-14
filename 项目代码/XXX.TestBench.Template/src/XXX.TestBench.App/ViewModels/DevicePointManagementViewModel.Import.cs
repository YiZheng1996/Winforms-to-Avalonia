using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class DevicePointManagementViewModel
{
    /// <summary>
    /// 解析、解析设备/分组、驱动预校验并生成统一导入计划。预览和最终应用共用该计划。
    /// </summary>
    public async Task<DevicePointImportPlan?> CreateImportPlanAsync(
        string filePath,
        CancellationToken ct = default)
    {
        StatusMessage = string.Empty;
        IsBusy = true;
        NotifyOperationStateChanged();
        try
        {
            var parsed = await _services.DevicePointImporter.ImportAsync(filePath, ct);
            if (!parsed.IsValid)
            {
                StatusMessage = FormatImportIssues(parsed);
                return null;
            }
            if (!IsGroupedConfiguration)
                throw new DomainException("当前设备点位配置不是完整配置版本，不能导入；请先生成当前生效配置");

            var prepared = PrepareImportedPoints(parsed.Points);
            var resolved = ResolveImportedGroups(new DevicePointImportResult(
                prepared,
                Array.Empty<DevicePointImportIssue>(),
                parsed.RowsWithNumbers));
            if (!resolved.IsValid)
                throw new DomainException(FormatImportIssues(resolved));

            var validated = ValidateImportedDriverAddresses(resolved);
            if (!validated.IsValid)
                throw new DomainException(FormatImportIssues(validated));

            var activePoints = _allEntries;
            var activeGroups = _allGroups;
            var sourceRevision = _services.DeviceModes.Runtime?.ActiveRevision ?? string.Empty;
            if (_services.DeviceConfigurations is not null)
            {
                var snapshot = await _services.DeviceConfigurations.LoadActiveAsync(ct);
                activePoints = snapshot.Points.Points;
                activeGroups = snapshot.Points.Groups;
                sourceRevision = snapshot.Revision;
            }

            var plan = new DevicePointImportPlanner()
                .Build(validated.RowsWithNumbers, activePoints, activeGroups) with
            {
                SourceRevision = sourceRevision,
                SourceCatalogFingerprint = DevicePointImportPlanner.ComputeCatalogFingerprint(activePoints)
            };
            StatusMessage = plan.CanApply
                ? $"文件校验通过：新增 {plan.AddedCount}、更新 {plan.UpdatedCount + plan.MovedCount}、无变化 {plan.UnchangedCount}，共 {plan.Rows.Count} 行"
                : $"导入预览存在 {plan.ConflictCount} 个冲突，不能应用";
            return plan;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "导入预览已取消";
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage = "导入预览未生成：" + ex.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
            NotifyOperationStateChanged();
        }
    }

    /// <summary>
    /// 应用同一个已固化 Plan。预览后 Active Revision 或目录指纹变化时拒绝旧计划。
    /// </summary>
    public Task<OperationFeedback> ApplyImportedPointsAsync(
        DevicePointImportPlan plan,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return RunMutationAsync(async token =>
        {
            if (!plan.CanApply)
                throw new DomainException("导入计划存在冲突，不能应用");
            if (_services.DeviceConfigurations is null)
                throw new DomainException("完整设备配置服务未连接，不能应用导入");

            var current = await _services.DeviceConfigurations.LoadActiveAsync(token);
            if (!string.IsNullOrWhiteSpace(plan.SourceRevision)
                && !string.Equals(plan.SourceRevision, current.Revision, StringComparison.Ordinal))
                throw new DomainException("配置已变化，请重新导入预览");
            var currentFingerprint = DevicePointImportPlanner.ComputeCatalogFingerprint(current.Points.Points);
            if (!string.Equals(plan.SourceCatalogFingerprint, currentFingerprint, StringComparison.Ordinal))
                throw new DomainException("设备点位目录已变化，请重新导入预览");

            return await SaveEntriesCoreAsync(
                plan.MergedPoints,
                $"已合并导入并应用：处理 {plan.Rows.Count} 行点位",
                current.Points.Groups,
                null,
                token,
                s7OptimizedBlockAccessConfirmed);
        }, ct);
    }

    /// <summary>
    /// 兼容旧调用方的已校验结果入口；内部仍转换为统一 Plan 后应用。
    /// </summary>
    public async Task<OperationFeedback> ApplyImportedPointsAsync(
        DevicePointImportResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsValid)
            return SetFeedback(false, FormatImportIssues(result));

        var sourcePoints = _allEntries;
        var sourceGroups = _allGroups;
        var sourceRevision = string.Empty;
        if (_services.DeviceConfigurations is not null)
        {
            var current = await _services.DeviceConfigurations.LoadActiveAsync(ct);
            sourcePoints = current.Points.Points;
            sourceGroups = current.Points.Groups;
            sourceRevision = current.Revision;
        }
        var plan = new DevicePointImportPlanner()
            .Build(result.RowsWithNumbers, sourcePoints, sourceGroups) with
        {
            SourceRevision = sourceRevision,
            SourceCatalogFingerprint = DevicePointImportPlanner.ComputeCatalogFingerprint(sourcePoints)
        };
        return await ApplyImportedPointsAsync(plan, ct);
    }

    public void CancelImportPreview()
        => SetFeedback(false, "已取消导入，当前设备点位未改变");
}
