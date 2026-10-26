using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EcoPort.Cloud.Core.Auth;

/// <summary>
/// Device request signature (contracts/device-v1/README.md):
/// <c>Base64(HMAC-SHA256(secret, METHOD \n PATH \n X-Timestamp \n lowercase-hex(SHA256(body))))</c>.
/// </summary>
public static class DeviceRequestSigner
{
    /// <summary>Largest allowed difference between <c>X-Timestamp</c> and the server clock.</summary>
    public static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    /// <summary>The only accepted <c>X-Timestamp</c> format: RFC 3339 UTC, whole seconds.</summary>
    public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string ComputeSignature(
        byte[] secret, string method, string path, string timestamp, ReadOnlySpan<byte> body) =>
        Convert.ToBase64String(ComputeMac(secret, method, path, timestamp, body));

    /// <summary>
    /// True only when every input is present, the timestamp parses and is within
    /// <see cref="MaxSkew"/> of <paramref name="now"/>, and the signature matches (constant-time).
    /// </summary>
    public static bool Verify(
        byte[]? secret,
        string method,
        string path,
        string? timestamp,
        string? signature,
        ReadOnlySpan<byte> body,
        DateTimeOffset now)
    {
        if (secret is null || secret.Length == 0 || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(
                timestamp,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var sent))
        {
            return false;
        }

        if ((now - sent).Duration() > MaxSkew)
        {
            return false;
        }

        Span<byte> provided = stackalloc byte[HMACSHA256.HashSizeInBytes];
        if (!Convert.TryFromBase64String(signature, provided, out var written) || written != provided.Length)
        {
            return false;
        }

        var expected = ComputeMac(secret, method, path, timestamp, body);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private static byte[] ComputeMac(byte[] secret, string method, string path, string timestamp, ReadOnlySpan<byte> body)
    {
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(body));
        var canonical = $"{method}\n{path}\n{timestamp}\n{bodyHash}";
        return HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(canonical));
    }
}
