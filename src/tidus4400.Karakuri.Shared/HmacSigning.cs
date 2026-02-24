using System.Security.Cryptography;
using System.Text;

namespace tidus4400.Karakuri.Shared;

public static class HmacSigning
{
    public static string ComputeBodySha256Hex(byte[] body)
    {
        var hash = SHA256.HashData(body);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string BuildCanonicalString(string method, string path, string timestampUnixSeconds, string bodySha256Hex)
    {
        return $"{method.ToUpperInvariant()}\n{path}\n{timestampUnixSeconds}\n{bodySha256Hex}";
    }

    public static string ComputeSignatureBase64(string secretBase64, string canonicalString)
    {
        var key = Convert.FromBase64String(secretBase64);
        using var hmac = new HMACSHA256(key);
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalString));
        return Convert.ToBase64String(sig);
    }

    public static bool ConstantTimeEqualsBase64(string leftBase64, string rightBase64)
    {
        try
        {
            var left = Convert.FromBase64String(leftBase64);
            var right = Convert.FromBase64String(rightBase64);
            return CryptographicOperations.FixedTimeEquals(left, right);
        }
        catch
        {
            return false;
        }
    }

    public static string HashSecretForStorage(string secretBase64)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secretBase64));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
