using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using EcoPort.Cloud.Contracts;
using Microsoft.AspNetCore.Components;

namespace EcoPort.Web.Services;

/// <summary>
/// <see cref="IEcoPortApi"/> over HTTP. URLs are relative, so <see cref="HttpClient.BaseAddress"/> is the
/// site's own origin (Static Web Apps serves the managed-functions API under <c>/api</c>).
/// Non-success responses throw <see cref="HttpRequestException"/>. An HTML response means SWA sent the
/// login page instead (signed out or session expired): the client then goes to <c>/login.html</c> with a
/// full page load and throws <see cref="SignedOutException"/>.
/// </summary>
public sealed class EcoPortApiClient(HttpClient http, NavigationManager? navigation = null) : IEcoPortApi
{
    public async Task<IReadOnlyList<DeviceSummaryDto>> GetDevicesAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<DeviceSummaryDto>>("api/devices", ct) ?? [];

    public async Task<DeviceStateDto?> GetStateAsync(string deviceId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/devices/{Uri.EscapeDataString(deviceId)}/state", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadJsonAsync<DeviceStateDto>(response, ct);
    }

    public async Task<IReadOnlyList<TelemetryPointDto>> GetTelemetryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var url = $"api/devices/{Uri.EscapeDataString(deviceId)}/telemetry?from={Iso(from)}&to={Iso(to)}";
        return await GetJsonAsync<List<TelemetryPointDto>>(url, ct) ?? [];
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        return await ReadJsonAsync<T>(response, ct);
    }

    private async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType == "text/html")
        {
            navigation?.NavigateTo("/login.html", forceLoad: true);
            throw new SignedOutException();
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options, ct);
    }

    /// <summary>UTC, RFC 3339 with seconds; contains no characters that need escaping in a query string.</summary>
    private static string Iso(DateTimeOffset t) =>
        t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>The User API answered with the login page: the visitor is signed out or lacks role bench.</summary>
public sealed class SignedOutException() : Exception("Signed out: sign in again to load data.");
