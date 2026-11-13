using System.Security.Cryptography;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Infrastructure.Configuration;

namespace XXX.TestBench.Upgrade;

/// <summary>Offline check and copy-only writer for the supported configuration migrations.</summary>
public sealed class ConfigurationUpgradeService
{
    public async Task<UpgradeReport> CheckAsync(
        string sourcePath,
        string sourceKind,
        string pointerSelection = "active",
        CancellationToken ct = default)
    {
        var report = new UpgradeReport { Command = "check-config", SourcePath = Path.GetFullPath(sourcePath) };
        try
        {
            var input = await LegacyConfigurationReader.ReadAsync(sourcePath, sourceKind, pointerSelection, ct);
            FillInput(report, input);
            var plan = ConfigurationUpgradePlanner.Plan(input);
            FillPlan(report, plan);
            var after = await LegacyConfigurationReader.RehashSourceAsync(input, pointerSelection, ct);
            if (!SameHashes(input.FileHashes, after))
            {
                report.Blockers.Add("检查期间来源配置文件发生变化；请停止写入后重新检查");
                report.FailureCode = 5;
            }
            else
            {
                report.FailureCode = plan.FailureCode;
                report.Success = report.Blockers.Count == 0;
            }
            report.Changes.Add("check-config 仅读取显式输入，不创建 revision、不切换 active、不启动运行时");
        }
        catch (UpgradeCommandException ex)
        {
            report.Blockers.Add(ex.Message);
            report.FailureCode = ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            report.Blockers.Add("操作已取消");
            report.FailureCode = 1;
        }
        catch (IOException ex)
        {
            report.Blockers.Add(ex.Message);
            report.FailureCode = 5;
        }
        catch (Exception ex)
        {
            report.Blockers.Add($"{ex.GetType().Name}: {ex.Message}");
            report.FailureCode = 1;
        }
        return report;
    }

    public async Task<UpgradeReport> ConvertAsync(
        string sourcePath,
        string sourceKind,
        string outputPath,
        string pointerSelection = "active",
        CancellationToken ct = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var output = Path.IsPathFullyQualified(outputPath) ? Path.GetFullPath(outputPath) : outputPath;
        var report = new UpgradeReport { Command = "convert-config", SourcePath = source, OutputPath = output };
        string? ownedLockPath = null;
        try
        {
            if (!Path.IsPathFullyQualified(outputPath))
                throw new UpgradeCommandException(2, "--output 必须是绝对路径");
            if (PathsOverlap(source, output))
                throw new UpgradeCommandException(2, "配置来源与输出目录相同或互相包含，拒绝写入");
            if (Directory.Exists(output) || File.Exists(output))
                throw new UpgradeCommandException(2, "输出路径已存在，拒绝覆盖");

            var input = await LegacyConfigurationReader.ReadAsync(source, sourceKind, pointerSelection, ct);
            FillInput(report, input);
            var plan = ConfigurationUpgradePlanner.Plan(input);
            FillPlan(report, plan);
            if (plan.FailureCode != 0)
            {
                report.FailureCode = plan.FailureCode;
                return report;
            }
            if (plan.NoChange)
            {
                report.Success = true;
                report.FailureCode = 0;
                report.Changes.Add("NoChange：未创建输出目录或 revision");
                return report;
            }

            var outputParent = Path.GetDirectoryName(output);
            if (string.IsNullOrWhiteSpace(outputParent))
                throw new UpgradeCommandException(2, "无法确定输出目录的父路径");
            Directory.CreateDirectory(outputParent);
            var lockPath = output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".upgrade.lock";
            using var outputLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            ownedLockPath = lockPath;
            if (Directory.Exists(output) || File.Exists(output))
                throw new UpgradeCommandException(2, "输出路径在创建前被占用，拒绝覆盖");

            var before = await LegacyConfigurationReader.RehashSourceAsync(input, pointerSelection, ct);
            if (!SameHashes(input.FileHashes, before))
                throw new UpgradeCommandException(5, "转换开始前来源配置文件发生变化；未写入输出");

            Directory.CreateDirectory(output);
            report.OutputWritten = true;
            if (Directory.EnumerateFileSystemEntries(output).Any())
                throw new UpgradeCommandException(2, "输出目录创建时发现并发写入内容；拒绝继续");
            var snapshot = plan.Snapshot ?? throw new InvalidOperationException("通过的转换计划缺少候选快照");
            await using (var store = new DeviceConfigurationStore(output))
            {
                await store.StageAsync(snapshot, ct);
                await store.CommitActiveAsync(snapshot.Revision, ct);
            }

            await using (var verifier = new DeviceConfigurationStore(output))
            {
                var verified = await verifier.LoadActiveAsync(ct);
                if (!string.Equals(verified.Revision, snapshot.Revision, StringComparison.Ordinal))
                    throw new InvalidDataException("重新读取输出 active 后 revision 不匹配");
            }

            var after = await LegacyConfigurationReader.RehashSourceAsync(input, pointerSelection, ct);
            if (!SameHashes(input.FileHashes, after))
                throw new InvalidDataException("转换期间来源配置文件哈希变化，输出不得投入使用");

            var outputFiles = ConfigurationUpgradePlanner.SerializeSnapshot(snapshot);
            foreach (var pair in outputFiles)
                report.OutputFileSha256[pair.Key] = Hash(pair.Value);
            report.OutputSha256 = HashFileSet(outputFiles);
            report.OutputVersion = null;
            report.OutputWritten = true;
            report.Success = true;
            report.FailureCode = 0;
            report.Changes.Add($"已写入独立输出目录并校验 active revision：{snapshot.Revision}");
        }
        catch (UpgradeCommandException ex)
        {
            report.Blockers.Add(ex.Message);
            report.FailureCode = ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            report.Blockers.Add("操作已取消；若输出目录已创建，内容不可投入使用");
            report.FailureCode = 1;
        }
        catch (IOException ex)
        {
            report.Blockers.Add($"I/O 或文件占用失败：{ex.Message}");
            report.FailureCode = 5;
        }
        catch (Exception ex)
        {
            report.Blockers.Add($"转换失败，来源未修改；若输出已写入则不得投入使用：{ex.GetType().Name}: {ex.Message}");
            report.FailureCode = ex is InvalidDataException ? 4 : 1;
        }
        finally
        {
            if (ownedLockPath is not null)
                try { if (File.Exists(ownedLockPath)) File.Delete(ownedLockPath); } catch { }
        }
        return report;
    }

    private static void FillInput(UpgradeReport report, LegacyConfigurationInput input)
    {
        report.SourcePath = input.SourceRoot;
        report.SourceSha256 = input.CombinedHash;
        report.InputFileSha256.Clear();
        foreach (var pair in input.FileHashes)
            report.InputFileSha256[pair.Key] = pair.Value;
        report.Changes.Add($"输入模式：{input.SourceKind}；指针选择：{input.PointerSelection}");
    }

    private static void FillPlan(UpgradeReport report, ConfigurationUpgradePlan plan)
    {
        report.SourceConfigurationVersions = plan.SourceVersions;
        report.OutputConfigurationVersions = plan.OutputVersions;
        report.SourceVersion = null;
        report.OutputVersion = null;
        foreach (var pair in plan.BeforeCounts)
            report.BeforeRowCounts[pair.Key] = pair.Value;
        foreach (var pair in plan.AfterCounts)
            report.AfterRowCounts[pair.Key] = pair.Value;
        report.Changes.AddRange(plan.Changes);
        report.Warnings.AddRange(plan.Warnings);
        report.Blockers.AddRange(plan.Blockers);
    }

    private static bool SameHashes(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var hash)
            && string.Equals(pair.Value, hash, StringComparison.OrdinalIgnoreCase));

    private static string HashFileSet(IReadOnlyDictionary<string, byte[]> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(pair.Key));
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(pair.Value);
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool PathsOverlap(string source, string output)
    {
        var sourcePath = ResolveExistingAncestor(source);
        var outputPath = ResolveExistingAncestor(output);
        return IsSameOrChild(sourcePath, outputPath) || IsSameOrChild(outputPath, sourcePath);
    }

    private static bool IsSameOrChild(string parent, string candidate)
    {
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedParent, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveExistingAncestor(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = new DirectoryInfo(full);
        var suffix = new Stack<string>();
        while (!directory.Exists && directory.Parent is not null)
        {
            suffix.Push(directory.Name);
            directory = directory.Parent;
        }
        var resolved = directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName;
        while (suffix.TryPop(out var child))
            resolved = Path.Combine(resolved, child);
        return Path.GetFullPath(resolved);
    }
}
