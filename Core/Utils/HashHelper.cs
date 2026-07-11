using System.Security.Cryptography;
using System.Text;

namespace RSSMagnetCatcher.Core.Utils;

public static class HashHelper
{
    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
