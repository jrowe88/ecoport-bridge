using System.Globalization;
using EcoPort.Cloud.Contracts;
using EcoPort.Cloud.Core.Storage;

namespace EcoPort.Cloud.Core.Read;

/// <summary>Status code plus value or error message for a user API call.</summary>
public sealed record ApiResult<T>(int StatusCode, T? Value, string? Error)
{
    public static ApiResult<T> Ok(T value) => new(200, value, null);

    public static ApiResult<T> NotFound() => new(404, default, null);

    public static ApiResult<T> BadRequest(string error) => new(400, default, error);
}

/// <summary>
/// The user API (<c>/api/devices…</c>) without the Functions plumbing. Access control is done by
/// Static Web Apps route rules (role <c>bench</c>) in milestone 1.
/// </summary>
public sealed class DeviceQueries(ITableStore store, TimeProvider time)
{
    public const int MaxTelemetryPoints = 500;
    public static readonly TimeSpan MaxTelemetryRange = TimeSpan.FromHours(24);

    /// <summary><c>GET /api/devices</c>, ordered by device ID.</summary>
    public async Task<IReadOnlyList<DeviceSummaryDto>> ListDevicesAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var result = new List<DeviceSummaryDto>();
        await foreach (var device in store.ListDevicesAsync(ct).ConfigureAwait(false))
        {
            var state = await store.GetCurrentStateAsync(device.RowKey, ct).ConfigureAwait(false);
            var lastSeen = state?.LastSeen;
            result.Add(new DeviceSummaryDto(device.RowKey, lastSeen, Connectivity.From(lastSeen, now), state?.Simulated ?? false));
        }

        return result.OrderBy(d => d.DeviceId, StringComparer.Ordinal).ToList();
    }

    /// <summary><c>GET /api/devices/{deviceId}/state</c>; 404 when the device is not registered.</summary>
    public async Task<ApiResult<DeviceStateDto>> GetStateAsync(string deviceId, CancellationToken ct)
    {
        if (await store.GetDeviceAsync(deviceId, ct).ConfigureAwait(false) is null)
        {
            return ApiResult<DeviceStateDto>.NotFound();
        }

        var s = await store.GetCurrentStateAsync(deviceId, ct).ConfigureAwait(false);
        var lastSeen = s?.LastSeen;
        return ApiResult<DeviceStateDto>.Ok(new DeviceStateDto(
            deviceId,
            lastSeen,
            Connectivity.From(lastSeen, time.GetUtcNow()),
            s?.Simulated ?? false,
            s?.SampleTime,
            s?.OpState,
            s?.ElectricityRateW,
            s?.TotalStorageCapacityWh,
            s?.PresentEnergyTakeWh,
            StoredEnergy.Percent(s?.TotalStorageCapacityWh, s?.PresentEnergyTakeWh)));
    }

    /// <summary>
    /// <c>GET /api/devices/{deviceId}/telemetry?from=&amp;to=</c> (ISO 8601). Defaults: <c>to</c> = now,
    /// <c>from</c> = <c>to</c> − 24 h. Ranges over 24 h, unparsable or reversed → 400. At most
    /// <see cref="MaxTelemetryPoints"/> points.
    /// </summary>
    public async Task<ApiResult<IReadOnlyList<TelemetryPointDto>>> GetTelemetryAsync(
        string deviceId, string? from, string? to, CancellationToken ct)
    {
        DateTimeOffset toTime = time.GetUtcNow();
        if (!string.IsNullOrEmpty(to) && !TryParse(to, out toTime))
        {
            return ApiResult<IReadOnlyList<TelemetryPointDto>>.BadRequest("'to' is not an ISO 8601 date/time.");
        }

        var fromTime = toTime - MaxTelemetryRange;
        if (!string.IsNullOrEmpty(from) && !TryParse(from, out fromTime))
        {
            return ApiResult<IReadOnlyList<TelemetryPointDto>>.BadRequest("'from' is not an ISO 8601 date/time.");
        }

        if (fromTime >= toTime)
        {
            return ApiResult<IReadOnlyList<TelemetryPointDto>>.BadRequest("'from' must be before 'to'.");
        }

        if (toTime - fromTime > MaxTelemetryRange)
        {
            return ApiResult<IReadOnlyList<TelemetryPointDto>>.BadRequest("The range may be at most 24 hours.");
        }

        if (await store.GetDeviceAsync(deviceId, ct).ConfigureAwait(false) is null)
        {
            return ApiResult<IReadOnlyList<TelemetryPointDto>>.NotFound();
        }

        var points = new List<TelemetryPointDto>();
        await foreach (var e in store.QueryTelemetryAsync(deviceId, fromTime, toTime, ct).ConfigureAwait(false))
        {
            points.Add(new TelemetryPointDto(
                e.T,
                e.OpState,
                e.ElectricityRateW,
                StoredEnergy.Percent(e.TotalStorageCapacityWh, e.PresentEnergyTakeWh)));
        }

        return ApiResult<IReadOnlyList<TelemetryPointDto>>.Ok(TelemetryDownsampler.Downsample(points, MaxTelemetryPoints));
    }

    private static bool TryParse(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
}
