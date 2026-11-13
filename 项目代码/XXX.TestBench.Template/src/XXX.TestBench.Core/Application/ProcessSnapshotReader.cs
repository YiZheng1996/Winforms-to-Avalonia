using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

public enum ProcessDataState
{
    Unbound,
    Waiting,
    Good,
    Stale,
    Bad,
    Disconnected,
    InvalidBinding
}

public enum ProcessWriteState
{
    Idle,
    Confirming,
    Writing,
    AwaitingFeedback,
    Succeeded,
    Failed,
    Uncertain
}

public enum PipePressureState
{
    Unknown,
    Unpressurized,
    Pressurized
}

/// <summary>
/// 工艺页面一次统一刷新使用的工程值样本。
/// Revision 和 ConnectionGeneration 是样本有效性的组成部分，不能省略比较。
/// </summary>
public sealed record ProcessSample(
    string SignalKey,
    string? PointId,
    object? EngineeringValue,
    ProcessDataState State,
    DateTime? TimestampUtc,
    string Revision,
    long ConnectionGeneration);

/// <summary>
/// 从当前运行时缓存构造工艺信号快照。
/// </summary>
public sealed class ProcessSnapshotReader : IProcessSnapshotReader
{
    public IReadOnlyDictionary<string, ProcessSample> Capture(IDeviceRuntime runtime, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var revision = runtime.ActiveRevision ?? string.Empty;
        var bindings = runtime.SignalBindings?.Bindings
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, ProcessSample>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in ProcessSignalCatalog.All)
        {
            if (!bindings.TryGetValue(definition.SignalKey, out var pointId)
                || string.IsNullOrWhiteSpace(pointId))
            {
                result[definition.SignalKey] = new ProcessSample(
                    definition.SignalKey,
                    null,
                    null,
                    ProcessDataState.Unbound,
                    null,
                    revision,
                    0);
                continue;
            }

            pointId = pointId.Trim();
            var point = runtime.GetPoint(pointId);
            if (point is null)
            {
                result[definition.SignalKey] = new ProcessSample(
                    definition.SignalKey,
                    pointId,
                    null,
                    ProcessDataState.InvalidBinding,
                    null,
                    revision,
                    0);
                continue;
            }

            if (!definition.AcceptedDataTypes.Contains(point.DataType))
            {
                result[definition.SignalKey] = new ProcessSample(
                    definition.SignalKey,
                    pointId,
                    null,
                    ProcessDataState.InvalidBinding,
                    null,
                    revision,
                    0);
                continue;
            }

            var status = runtime.GetDeviceStatus(point.DeviceId);
            if (status.Health is DeviceHealth.Disconnected or DeviceHealth.Faulted
                || status.ConnectionState is DeviceConnectionState.Offline
                    or DeviceConnectionState.Faulted
                    or DeviceConnectionState.Disabled)
            {
                result[definition.SignalKey] = new ProcessSample(
                    definition.SignalKey,
                    pointId,
                    null,
                    ProcessDataState.Disconnected,
                    null,
                    revision,
                    status.ConnectionGeneration);
                continue;
            }

            if (!status.IsConnected
                || status.ConnectionState == DeviceConnectionState.Connecting
                || status.Health == DeviceHealth.Unknown)
            {
                result[definition.SignalKey] = new ProcessSample(
                    definition.SignalKey,
                    pointId,
                    null,
                    ProcessDataState.Waiting,
                    null,
                    revision,
                    status.ConnectionGeneration);
                continue;
            }

            if (!runtime.TryGetCachedValue(pointId, out var value))
            {
                result[definition.SignalKey] = new ProcessSample(
                    definition.SignalKey,
                    pointId,
                    null,
                    ProcessDataState.Waiting,
                    null,
                    revision,
                    status.ConnectionGeneration);
                continue;
            }

            var state = GetState(definition, point, value, status, revision, utcNow);
            result[definition.SignalKey] = new ProcessSample(
                definition.SignalKey,
                pointId,
                state == ProcessDataState.Good ? value.Value : null,
                state,
                value.TimestampUtc,
                value.Revision,
                value.ConnectionGeneration == 0 ? status.ConnectionGeneration : value.ConnectionGeneration);
        }

        return result;
    }

    private static ProcessDataState GetState(
        ProcessSignalDefinition definition,
        DevicePoint point,
        PointValue value,
        DeviceRuntimeInfo status,
        string revision,
        DateTime utcNow)
    {
        if (value.Quality == PointQuality.Bad)
            return ProcessDataState.Bad;
        if (value.Quality != PointQuality.Good)
            return ProcessDataState.Stale;
        if (!string.IsNullOrWhiteSpace(revision)
            && !string.Equals(value.Revision, revision, StringComparison.Ordinal))
            return ProcessDataState.InvalidBinding;
        if (!string.Equals(value.PointId, point.PointId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(value.DeviceId, point.DeviceId, StringComparison.OrdinalIgnoreCase))
            return ProcessDataState.InvalidBinding;
        if (value.ConnectionGeneration != 0
            && status.ConnectionGeneration != 0
            && value.ConnectionGeneration != status.ConnectionGeneration)
            return ProcessDataState.InvalidBinding;
        if (value.TimestampUtc > utcNow || value.TimestampUtc < DateTime.UnixEpoch)
            return ProcessDataState.InvalidBinding;
        if (utcNow - value.TimestampUtc > definition.MaxSampleAge)
            return ProcessDataState.Stale;
        if (definition.IsNumeric && value.Value is not null && !IsNumeric(value.Value))
            return ProcessDataState.Bad;
        return ProcessDataState.Good;
    }

    private static bool IsNumeric(object value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal;
}
