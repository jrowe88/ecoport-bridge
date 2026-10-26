using Microsoft.Extensions.Configuration;

namespace EcoPort.Cloud.Core.Auth;

/// <summary>
/// Local development store: reads <c>DeviceSecrets:{deviceId}</c> (Base64) from configuration,
/// e.g. <c>DeviceSecrets__WH001</c> in <c>local.settings.json</c>.
/// </summary>
public sealed class ConfigDeviceSecretStore(IConfiguration configuration) : IDeviceSecretStore
{
    public Task<byte[]?> GetSecretAsync(string deviceId, CancellationToken ct)
    {
        if (!DeviceIds.IsValid(deviceId))
        {
            return Task.FromResult<byte[]?>(null);
        }

        var value = configuration[$"DeviceSecrets:{deviceId}"];
        return Task.FromResult(SecretEncoding.Decode(value));
    }
}

internal static class SecretEncoding
{
    public static byte[]? Decode(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64.Trim());
            return bytes.Length == 0 ? null : bytes;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
