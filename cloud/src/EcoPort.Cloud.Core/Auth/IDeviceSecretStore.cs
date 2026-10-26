using System.Text.RegularExpressions;

namespace EcoPort.Cloud.Core.Auth;

/// <summary>Looks up a device's 32-byte HMAC secret.</summary>
public interface IDeviceSecretStore
{
    /// <returns>The secret, or null when the device has none (or the stored value is unusable).</returns>
    Task<byte[]?> GetSecretAsync(string deviceId, CancellationToken ct);
}

/// <summary>Device ID rules shared by the secret stores and the device endpoint.</summary>
public static partial class DeviceIds
{
    /// <summary>Letters, digits and dashes, 1–64 characters (also a valid Key Vault secret-name fragment).</summary>
    public static bool IsValid(string? deviceId) => deviceId is not null && Pattern().IsMatch(deviceId);

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
