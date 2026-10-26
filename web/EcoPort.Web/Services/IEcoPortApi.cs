using EcoPort.Cloud.Contracts;

namespace EcoPort.Web.Services;

/// <summary>The User API (<c>/api/*</c>) as the dashboard sees it.</summary>
public interface IEcoPortApi
{
    /// <summary><c>GET /api/devices</c>.</summary>
    Task<IReadOnlyList<DeviceSummaryDto>> GetDevicesAsync(CancellationToken ct = default);

    /// <summary><c>GET /api/devices/{deviceId}/state</c>; null when the device is unknown (404).</summary>
    Task<DeviceStateDto?> GetStateAsync(string deviceId, CancellationToken ct = default);

    /// <summary><c>GET /api/devices/{deviceId}/telemetry?from=&amp;to=</c> (range at most 24 h).</summary>
    Task<IReadOnlyList<TelemetryPointDto>> GetTelemetryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
