using System.Security.Cryptography;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Identity;

/// <summary>
/// PBKDF2-SHA256 密码哈希（格式：iterations.salt.hash，均为 Base64）。
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>
    /// 散列迭代次数。
    /// </summary>
    private const int Iterations = 100_000;
    /// <summary>
    /// 随机盐长度。
    /// </summary>
    private const int SaltSize = 16;
    /// <summary>
    /// 散列输出长度。
    /// </summary>
    private const int HashSize = 32;

    /// <summary>
    /// 生成带随机盐的密码散列。
    /// </summary>
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// 校验密码与存储散列是否一致。
    /// </summary>
    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }
        var candidate = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(candidate, expected);
    }
}
