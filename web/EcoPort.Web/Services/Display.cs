using System.Globalization;
using MudBlazor;

namespace EcoPort.Web.Services;

/// <summary>Formatting shared by the pages.</summary>
public static class Display
{
    public const string Dash = "—";

    /// <summary><c>online</c> green, <c>delayed</c> amber, anything else (<c>offline</c>) red.</summary>
    public static Color ConnectivityColor(string? connectivity) => connectivity switch
    {
        "online" => Color.Success,
        "delayed" => Color.Warning,
        _ => Color.Error,
    };

    /// <summary>CTA-2045 operational state: 0 Idle, 1 Running, other values as the raw number.</summary>
    public static string OpState(int? opState) => opState switch
    {
        null => Dash,
        0 => "Idle",
        1 => "Running",
        _ => opState.Value.ToString(CultureInfo.InvariantCulture),
    };

    public static string Watts(int? watts) =>
        watts is { } w ? string.Create(CultureInfo.CurrentCulture, $"{w:N0} W") : Dash;

    public static string Percent(double? percent) =>
        percent is { } p ? string.Create(CultureInfo.CurrentCulture, $"{p:0}%") : Dash;

    /// <summary>"just now", "40 s ago", "3 min ago", "2 h ago", "5 d ago"; "never" when null.</summary>
    public static string Ago(DateTimeOffset? t, DateTimeOffset now)
    {
        if (t is not { } seen)
        {
            return "never";
        }

        var age = now - seen;
        return age.TotalSeconds switch
        {
            < 5 => "just now",
            < 60 => $"{(int)age.TotalSeconds} s ago",
            < 3600 => $"{(int)age.TotalMinutes} min ago",
            < 86400 => $"{(int)age.TotalHours} h ago",
            _ => $"{(int)age.TotalDays} d ago",
        };
    }

    /// <summary>A short, readable reason for a failed API call.</summary>
    public static string LoadError(Exception ex) => ex switch
    {
        System.Text.Json.JsonException or NotSupportedException =>
            "the API didn't return JSON (signed out, or the API isn't running)",
        HttpRequestException { StatusCode: { } status } => $"the API returned HTTP {(int)status} {status}",
        HttpRequestException => "couldn't reach the API",
        _ => ex.Message,
    };

    /// <summary>Local date and time, short format.</summary>
    public static string LocalTime(DateTimeOffset? t) =>
        t is { } v ? v.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : Dash;
}
