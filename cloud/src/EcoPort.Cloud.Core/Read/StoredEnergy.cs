namespace EcoPort.Cloud.Core.Read;

/// <summary>Stored energy as a percentage of capacity, computed at read time from raw Commodity values.</summary>
public static class StoredEnergy
{
    /// <returns>
    /// <c>(1 − take / capacity) × 100</c>, clamped to 0–100; null when capacity ≤ 0 (the heater
    /// reports 0 in vacation mode).
    /// </returns>
    public static double? Percent(int capacityWh, int takeWh)
    {
        if (capacityWh <= 0)
        {
            return null;
        }

        var percent = (1.0 - (double)takeWh / capacityWh) * 100.0;
        return Math.Clamp(percent, 0.0, 100.0);
    }

    /// <summary>Null when either value is missing.</summary>
    public static double? Percent(int? capacityWh, int? takeWh) =>
        capacityWh is { } c && takeWh is { } t ? Percent(c, t) : null;
}
