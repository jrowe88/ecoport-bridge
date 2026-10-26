using System.Text.Json;
using EcoPort.Cloud.Contracts;
using EcoPort.Cloud.Core.Auth;
using EcoPort.Cloud.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EcoPort.Cloud.Core.Ingest;

/// <summary>Status code and JSON body (null = empty) for a device API response.</summary>
public sealed record DeviceApiResult(int StatusCode, string? Json)
{
    public static DeviceApiResult Empty(int status) => new(status, null);

    public static DeviceApiResult Error(string message) =>
        new(400, JsonSerializer.Serialize(new ErrorResponse(message), JsonDefaults.Options));
}

/// <summary>
/// <c>POST /device/v1/records</c> without the Functions plumbing: size limit, authentication,
/// parsing, validation, ingest. 401 responses carry no detail; the reason is logged server-side.
/// </summary>
public sealed class DeviceUploadHandler(
    ITableStore store,
    IDeviceSecretStore secrets,
    RecordIngestor ingestor,
    TimeProvider time,
    ILogger<DeviceUploadHandler>? logger = null)
{
    public const int MaxBodyBytes = 64 * 1024;

    private readonly ILogger _logger = logger ?? NullLogger<DeviceUploadHandler>.Instance;

    public async Task<DeviceApiResult> HandleAsync(
        string method,
        string path,
        string? deviceId,
        string? timestamp,
        string? signature,
        ReadOnlyMemory<byte> body,
        CancellationToken ct)
    {
        if (body.Length > MaxBodyBytes)
        {
            return DeviceApiResult.Empty(413);
        }

        if (!DeviceIds.IsValid(deviceId))
        {
            _logger.LogWarning("Upload rejected: missing or malformed X-Device-Id.");
            return DeviceApiResult.Empty(401);
        }

        if (await store.GetDeviceAsync(deviceId!, ct).ConfigureAwait(false) is null)
        {
            _logger.LogWarning("Upload rejected: unknown device {DeviceId}.", deviceId);
            return DeviceApiResult.Empty(401);
        }

        var secret = await secrets.GetSecretAsync(deviceId!, ct).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (!DeviceRequestSigner.Verify(secret, method, path, timestamp, signature, body.Span, now))
        {
            _logger.LogWarning(
                "Upload rejected for {DeviceId}: {Reason} (X-Timestamp {Timestamp}, server time {Now:O}).",
                deviceId,
                secret is null ? "no secret configured" : "bad signature or timestamp skew",
                timestamp,
                now);
            return DeviceApiResult.Empty(401);
        }

        UploadRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<UploadRequest>(body.Span, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            return DeviceApiResult.Error($"Body is not a valid v1 upload: {ex.Message}");
        }

        if (req is null)
        {
            return DeviceApiResult.Error("Body is not a valid v1 upload: null.");
        }

        var error = UploadValidator.Validate(req, deviceId!, now);
        if (error is not null)
        {
            _logger.LogWarning("Upload from {DeviceId} failed validation: {Error}", deviceId, error);
            return DeviceApiResult.Error(error);
        }

        var response = await ingestor.IngestAsync(req, now, ct).ConfigureAwait(false);
        return new DeviceApiResult(200, JsonSerializer.Serialize(response, JsonDefaults.Options));
    }
}
