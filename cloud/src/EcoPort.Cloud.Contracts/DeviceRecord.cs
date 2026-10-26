using System.Text.Json;

namespace EcoPort.Cloud.Contracts;

/// <summary>Record kinds a device can send.</summary>
public static class RecordKinds
{
    public const string Sample = "sample";
    public const string Event = "event";
    public const string Health = "health";
}

/// <summary>
/// One record in an upload. Flat: the kind-specific members are null for other kinds.
/// <c>seq</c>, <c>kind</c> and <c>uptimeMs</c> are required by the JSON reader; everything else
/// is checked by the upload validator so the device gets a readable 400 message.
/// </summary>
public sealed record DeviceRecord(
    uint Seq,
    string Kind,
    ulong UptimeMs,
    DateTimeOffset? T = null,
    // sample
    int? OpState = null,
    CommodityDto? Commodity = null,
    LinkDto? Link = null,
    // event
    string? Type = null,
    JsonElement? Data = null,
    // health
    int? RssiDbm = null,
    uint? FreeHeapBytes = null,
    uint? BufferDepth = null,
    string? ResetReason = null);

/// <summary>CTA-2045 Commodity Read values, raw as the heater reports them.</summary>
public sealed record CommodityDto(
    int ElectricityRateW,
    int ElectricityCumulativeWh,
    int TotalStorageCapacityWh,
    int PresentEnergyTakeWh);

/// <summary>RS-485 link statistics for the poll that produced a sample.</summary>
public sealed record LinkDto(bool Ok, uint Retries, uint Naks);
