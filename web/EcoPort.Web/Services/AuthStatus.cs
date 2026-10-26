using System.Net.Http.Json;

namespace EcoPort.Web.Services;

/// <summary>Whether the current visitor may use the dashboard.</summary>
public interface IAuthStatus
{
    /// <summary>True when signed in with the <c>bench</c> role.</summary>
    Task<bool> HasBenchRoleAsync(CancellationToken ct = default);
}

/// <summary>
/// Asks Static Web Apps (<c>/.auth/me</c>). The installed PWA's service worker serves the app shell
/// from cache, so SWA's own "not signed in → login page" redirect never runs for page loads; this
/// check is what sends a signed-out visitor to the login page. Any failure counts as not allowed.
/// </summary>
public sealed class SwaAuthStatus(HttpClient http) : IAuthStatus
{
    public async Task<bool> HasBenchRoleAsync(CancellationToken ct = default)
    {
        try
        {
            var me = await http.GetFromJsonAsync<AuthMe>(".auth/me", ct);
            return me?.ClientPrincipal?.UserRoles?.Contains("bench") == true;
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private sealed record AuthMe(ClientPrincipal? ClientPrincipal);

    private sealed record ClientPrincipal(string? IdentityProvider, string? UserDetails, string[]? UserRoles);
}
