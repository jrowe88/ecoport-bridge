using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using EcoPort.Cloud.Contracts;

namespace EcoPort.Web.Services;

/// <summary>
/// <see cref="IEcoPortApi"/> over HTTP. URLs are relative, so <see cref="HttpClient.BaseAddress"/> is the
/// site's own origin (Static Web Apps serves the managed-functions API under <c>/api</c>).
/// Non-success responses throw <see cref="HttpRequestException"/>; a response that isn't JSON
/// (e.g. the SWA login page after a session expires) throws <see cref="System.Text.Json.JsonException"/>.
/// </summary>
public sealed class EcoPortApiClient(HttpClient http) : IEcoPortApi
{
    public async Task<IReadOnlyList<DeviceSummaryDto>> GetDevicesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<DeviceSummaryDto>>("api/devices", JsonDefaults.Options, ct) ?? [];

    public async Task<DeviceStateDto?> GetStateAsync(string deviceId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/devices/{Uri.EscapeDataString(deviceId)}/state", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceStateDto>(JsonDefaults.Options, ct);
    }

    public async Task<IReadOnlyList<TelemetryPointDto>> GetTelemetryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var url = $"api/devices/{Uri.EscapeDataString(deviceId)}/telemetry?from={Iso(from)}&to={Iso(to)}";
        return await http.GetFromJsonAsync<List<TelemetryPointDto>>(url, JsonDefaults.Options, ct) ?? [];
    }

    /// <summary>UTC, RFC 3339 with seconds; contains no characters that need escaping in a query string.</summary>
    private static string Iso(DateTimeOffset t) =>
        t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
