using System.Security.Cryptography;
using System.Text;

namespace XXX.TestBench.Core.Common;

/// <summary>
/// Provides deterministic identifiers for migration and import boundaries.
/// </summary>
public static class StableIdentity
{
    /// <summary>
    /// Computes the established deterministic GUID for a seed.
    /// The byte order, hash truncation, version bits, and formatting are persisted behavior.
    /// </summary>
    public static string FromSeed(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var guidBytes = bytes[..16].ToArray();
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes).ToString("D");
    }
}
