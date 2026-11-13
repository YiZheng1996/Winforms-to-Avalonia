namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 密码散列接口。
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// 生成密码散列值。
    /// </summary>
    string Hash(string password);
    /// <summary>
    /// 校验密码与散列值是否匹配。
    /// </summary>
    bool Verify(string password, string hash);
}
