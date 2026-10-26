using System.Collections.Concurrent;
using Azure;
using Azure.Security.KeyVault.Secrets;

namespace EcoPort.Cloud.Core.Auth;

/// <summary>
/// Reads Key Vault secret <c>device-{deviceId}</c> (Base64 of the 32-byte secret) and caches
/// found secrets for 10 minutes. Missing secrets are not cached, so a newly provisioned device
/// works straight away.
/// </summary>
public sealed class KeyVaultDeviceSecretStore(SecretClient client, TimeProvider time) : IDeviceSecretStore
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (byte[] Secret, DateTimeOffset Expires)> _cache = new();

    public static string SecretName(string deviceId) => $"device-{deviceId}";

    public async Task<byte[]?> GetSecretAsync(string deviceId, CancellationToken ct)
    {
        if (!DeviceIds.IsValid(deviceId))
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (_cache.TryGetValue(deviceId, out var cached) && cached.Expires > now)
        {
            return cached.Secret;
        }

        KeyVaultSecret secret;
        try
        {
            secret = (await client.GetSecretAsync(SecretName(deviceId), null, ct).ConfigureAwait(false)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _cache.TryRemove(deviceId, out _);
            return null;
        }

        var bytes = SecretEncoding.Decode(secret.Value);
        if (bytes is null)
        {
            _cache.TryRemove(deviceId, out _);
            return null;
        }

        _cache[deviceId] = (bytes, now + CacheDuration);
        return bytes;
    }
}
