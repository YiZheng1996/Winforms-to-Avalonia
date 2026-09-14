using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 从固定 SignalKey → PointId 绑定解析当前生效点位，并在使用前检查目标设备和样本质量。
/// </summary>
public sealed class SignalResolver
{
    private readonly IDeviceRuntime _runtime;
    private readonly IReadOnlyDictionary<string, string> _bindings;
    private readonly Func<DateTime> _utcNow;

    public SignalResolver(
        IDeviceRuntime runtime,
        IReadOnlyDictionary<string, string>? bindings = null,
        Func<DateTime>? utcNow = null)
    {
        _runtime = runtime;
        _bindings = bindings ?? runtime.SignalBindings.Bindings ?? new Dictionary<string, string>();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// 只解析身份、类型和单位，不执行读取。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ResolvedSignal>> ResolveAsync(
        IEnumerable<RequiredSignal> requirements,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        var points = await _runtime.ListPointsAsync(ct);
        var pointById = points
            .Where(point => point is not null && !string.IsNullOrWhiteSpace(point.PointId))
            .GroupBy(point => point.PointId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, ResolvedSignal>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var requirement in requirements)
        {
            if (requirement is null)
            {
                errors.Add("存在空的必需信号声明");
                continue;
            }

            var signalKey = requirement.SignalKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(signalKey))
            {
                errors.Add("必需信号 SignalKey 不能为空");
                continue;
            }
            if (result.TryGetValue(signalKey, out var alreadyResolved))
            {
                if (alreadyResolved.Requirement != requirement)
                    errors.Add($"执行器对信号 {signalKey} 声明了不一致的类型或用途");
                continue;
            }
            if (requirement.Access is not (SignalAccessKind.Read or SignalAccessKind.Write))
            {
                errors.Add($"信号 {signalKey} 的用途不受支持：{requirement.Access}");
                continue;
            }
            if (requirement.ExpectedDataType == DevicePointDataType.Unknown)
            {
                errors.Add($"信号 {signalKey} 未声明预期数据类型");
                continue;
            }
            if (requirement.MaxSampleAge <= TimeSpan.Zero)
            {
                errors.Add($"信号 {signalKey} 的最大样本年龄必须大于 0");
                continue;
            }

            var binding = FindBinding(signalKey);
            if (string.IsNullOrWhiteSpace(binding))
            {
                errors.Add($"必需信号 {signalKey} 尚未绑定 PointId");
                continue;
            }
            if (!Guid.TryParse(binding, out _))
            {
                errors.Add($"信号 {signalKey} 的 PointId 无效：{binding}");
                continue;
            }
            if (!pointById.TryGetValue(binding, out var point))
            {
                errors.Add($"信号 {signalKey} 绑定的点位不存在：{binding}");
                continue;
            }
            var compatible = true;
            if (!IsTypeCompatible(requirement.ExpectedDataType, point.DataType))
            {
                errors.Add($"信号 {signalKey} 类型不匹配：期望 {DevicePointTypeCatalog.ToDisplayName(requirement.ExpectedDataType)}，实际 {DevicePointTypeCatalog.ToDisplayName(point.DataType)}");
                compatible = false;
            }
            if (requirement.Access == SignalAccessKind.Write && !point.IsWritable)
            {
                errors.Add($"信号 {signalKey} 声明为写入信号，但目标点位不可写：{point.Code}");
                compatible = false;
            }
            if (!compatible) continue;

            result[signalKey] = new ResolvedSignal(signalKey, point.PointId, point, requirement);
        }

        if (errors.Count > 0)
            throw new SignalDependencyException(string.Join("；", errors));
        return result;
    }

    /// <summary>
    /// 启动前解析并读取全部必需信号，成功后返回可写入记录快照的映射。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ResolvedSignal>> PreflightAsync(
        IEnumerable<RequiredSignal> requirements,
        CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(requirements, ct);
        var errors = new List<string>();
        foreach (var signal in resolved.Values)
        {
            try
            {
                _ = await ReadFreshAsync(signal, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"信号 {signal.SignalKey}：{ex.Message}");
            }
        }

        if (errors.Count > 0)
            throw new SignalDependencyException(string.Join("；", errors));
        return resolved;
    }

    /// <summary>
    /// 执行期间按已固化映射读取一次新鲜样本。
    /// </summary>
    public Task<PointValue> ReadFreshAsync(
        string signalKey,
        IReadOnlyDictionary<string, ResolvedSignal> resolvedSignals,
        CancellationToken ct = default)
    {
        if (!resolvedSignals.TryGetValue(signalKey, out var signal))
            throw new SignalDependencyException($"信号 {signalKey} 不在本次试验的已解析映射中");
        return ReadFreshAsync(signal, ct);
    }

    private async Task<PointValue> ReadFreshAsync(ResolvedSignal signal, CancellationToken ct)
    {
        var current = _runtime.GetPoint(signal.PointId);
        if (current is null)
            throw new SignalDependencyException($"信号 {signal.SignalKey} 的点位已从当前运行时移除：{signal.PointId}");
        if (!string.Equals(current.PointId, signal.PointId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(signal.Point.DeviceId)
                && !string.Equals(current.DeviceId, signal.Point.DeviceId, StringComparison.OrdinalIgnoreCase)))
            throw new SignalDependencyException($"信号 {signal.SignalKey} 的点位归属已变化，拒绝使用旧映射");

        var status = GetTargetStatus(current);
        if (status.ConnectionState == DeviceConnectionState.Disabled
            || status.Health is not (DeviceHealth.Healthy or DeviceHealth.Degraded)
            || !status.IsConnected)
            throw new SignalDependencyException($"信号 {signal.SignalKey} 所属设备不可用：{status.DeviceId}/{status.Health}");

        PointValue sample;
        try
        {
            sample = await _runtime.ReadFreshAsync(signal.PointId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SignalDependencyException($"信号 {signal.SignalKey} 新鲜读取失败：{ex.Message}");
        }

        if (sample.Quality != PointQuality.Good)
            throw new SignalDependencyException($"信号 {signal.SignalKey} 质量不可用：{sample.Quality}");
        if (!string.IsNullOrWhiteSpace(sample.PointId)
            && !string.Equals(sample.PointId, signal.PointId, StringComparison.OrdinalIgnoreCase))
            throw new SignalDependencyException($"信号 {signal.SignalKey} 返回了错误的 PointId：{sample.PointId}");
        if (!string.IsNullOrWhiteSpace(sample.DeviceId)
            && !string.Equals(sample.DeviceId, signal.Point.DeviceId, StringComparison.OrdinalIgnoreCase))
            throw new SignalDependencyException($"信号 {signal.SignalKey} 返回了错误的 DeviceId：{sample.DeviceId}");
        if (!string.IsNullOrWhiteSpace(_runtime.ActiveRevision)
            && !string.IsNullOrWhiteSpace(sample.Revision)
            && !string.Equals(sample.Revision, _runtime.ActiveRevision, StringComparison.Ordinal))
            throw new SignalDependencyException($"信号 {signal.SignalKey} 返回了过期配置版本样本：{sample.Revision}");
        var age = _utcNow() - sample.TimestampUtc;
        if (age > signal.Requirement.MaxSampleAge)
            throw new SignalDependencyException($"信号 {signal.SignalKey} 样本已过期：{age.TotalMilliseconds:0}ms");
        return sample;
    }

    private string? FindBinding(string signalKey)
        => _bindings.FirstOrDefault(pair => string.Equals(pair.Key?.Trim(), signalKey, StringComparison.OrdinalIgnoreCase)).Value?.Trim();

    private DeviceRuntimeInfo GetTargetStatus(DevicePoint point)
        => string.IsNullOrWhiteSpace(point.DeviceId)
            ? _runtime.Status
            : _runtime.GetDeviceStatus(point.DeviceId);

    private static bool IsTypeCompatible(DevicePointDataType expected, DevicePointDataType actual)
        => expected == actual
           || (expected is DevicePointDataType.Boolean or DevicePointDataType.Bool
               && actual is DevicePointDataType.Boolean or DevicePointDataType.Bool);
}
