using System.Security.Cryptography;
using System.Text;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// 按内容摘要保存型号资源；引用目录同时绑定业务资源键和摘要。
/// </summary>
public sealed class FileModelResourceStore : IModelResourceStore
{
    private readonly string _root;

    public FileModelResourceStore(string resourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);
        if (!Path.IsPathFullyQualified(resourceRoot))
            throw new ArgumentException("型号资源根目录必须是绝对路径。", nameof(resourceRoot));
        _root = Path.GetFullPath(resourceRoot);
    }

    /// <summary>解析单一不可变资源键，并在返回摘要前校验对象内容。</summary>
    public async Task<string> ResolveUniqueHashAsync(string resourceKey, CancellationToken cancellationToken = default)
    {
        ValidateResourceKey(resourceKey);
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceKey)))
            .ToLowerInvariant();
        var referenceDirectory = Path.Combine(_root, "references", keyHash);
        if (!Directory.Exists(referenceDirectory))
            throw new FileNotFoundException("未找到型号资源引用。", referenceDirectory);

        var references = Directory.GetFiles(referenceDirectory, "*.ref", SearchOption.TopDirectoryOnly);
        if (references.Length != 1)
            throw new InvalidDataException("资源键必须且只能绑定一个不可变内容版本。");

        var hash = Path.GetFileNameWithoutExtension(references[0]);
        ValidateHash(hash);
        await using var stream = await OpenReadAsync(resourceKey, hash, cancellationToken);
        return hash;
    }

    /// <summary>
    /// 原子导入内容；相同字节可以被多个受控资源键引用。
    /// </summary>
    public async Task<string> ImportAsync(
        string resourceKey,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ValidateResourceKey(resourceKey);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
            throw new ArgumentException("资源流不可读。", nameof(content));

        var objectDirectory = Path.Combine(_root, "objects");
        Directory.CreateDirectory(objectDirectory);
        var temporaryObject = Path.Combine(objectDirectory, $".import-{Guid.NewGuid():N}.tmp");
        try
        {
            string hash;
            await using (var destination = new FileStream(temporaryObject, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                hash = await CopyAndHashAsync(content, destination, cancellationToken);
                if (destination.Length <= 0)
                    throw new InvalidDataException("型号资源不能为空文件。");
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            var objectPath = GetObjectPath(hash);
            if (File.Exists(objectPath))
            {
                await VerifyFileHashAsync(objectPath, hash, cancellationToken);
                File.Delete(temporaryObject);
            }
            else
            {
                try
                {
                    File.Move(temporaryObject, objectPath);
                }
                catch (IOException) when (File.Exists(objectPath))
                {
                    await VerifyFileHashAsync(objectPath, hash, cancellationToken);
                    File.Delete(temporaryObject);
                }
            }

            await PublishReferenceAsync(resourceKey, hash, cancellationToken);
            return hash;
        }
        finally
        {
            TryDeleteFile(temporaryObject);
        }
    }

    /// <summary>
    /// 按资源键和摘要读取不可变资源，并在返回前重新核对内容摘要。
    /// </summary>
    public async Task<Stream> OpenReadAsync(
        string resourceKey,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ValidateResourceKey(resourceKey);
        ValidateHash(expectedSha256);

        var referencePath = GetReferencePath(resourceKey, expectedSha256);
        if (!File.Exists(referencePath))
            throw new FileNotFoundException("未找到匹配键和摘要的型号资源引用。", referencePath);

        var reference = await File.ReadAllTextAsync(referencePath, cancellationToken);
        if (!string.Equals(reference, expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("型号资源引用内容与请求摘要不一致。");

        var objectPath = GetObjectPath(expectedSha256);
        var stream = new FileStream(objectPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
            if (!string.Equals(actualHash, expectedSha256, StringComparison.Ordinal))
                throw new InvalidDataException("型号资源内容摘要校验失败。");
            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private async Task PublishReferenceAsync(
        string resourceKey,
        string hash,
        CancellationToken cancellationToken)
    {
        var referencePath = GetReferencePath(resourceKey, hash);
        var referenceDirectory = Path.GetDirectoryName(referencePath)!;
        Directory.CreateDirectory(referenceDirectory);
        if (File.Exists(referencePath))
        {
            var savedHash = await File.ReadAllTextAsync(referencePath, cancellationToken);
            if (!string.Equals(savedHash, hash, StringComparison.Ordinal))
                throw new InvalidDataException("已有型号资源引用与内容版本冲突。");
            return;
        }

        var temporaryReference = Path.Combine(referenceDirectory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryReference, hash, new UTF8Encoding(false), cancellationToken);
            try
            {
                File.Move(temporaryReference, referencePath);
            }
            catch (IOException) when (File.Exists(referencePath))
            {
                var savedHash = await File.ReadAllTextAsync(referencePath, cancellationToken);
                if (!string.Equals(savedHash, hash, StringComparison.Ordinal))
                    throw new InvalidDataException("并发导入造成型号资源引用冲突。");
            }
        }
        finally
        {
            TryDeleteFile(temporaryReference);
        }
    }

    private string GetObjectPath(string hash) => Path.Combine(_root, "objects", hash + ".blob");

    private string GetReferencePath(string resourceKey, string hash)
    {
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceKey)))
            .ToLowerInvariant();
        return Path.Combine(_root, "references", keyHash, hash + ".ref");
    }

    private static async Task<string> CopyAndHashAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, count);
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task VerifyFileHashAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("内容寻址型号资源已损坏。");
    }

    private static void ValidateResourceKey(string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (resourceKey.Length > 512 || resourceKey.Any(char.IsControl))
            throw new ArgumentException("型号资源键无效。", nameof(resourceKey));
    }

    private static void ValidateHash(string hash)
    {
        if (hash is not { Length: 64 }
            || hash.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("资源摘要必须是 64 位小写 SHA-256。", nameof(hash));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // 清理临时文件失败不覆盖原始错误。
        }
        catch (UnauthorizedAccessException)
        {
            // 清理临时文件失败不覆盖原始错误。
        }
    }
}
