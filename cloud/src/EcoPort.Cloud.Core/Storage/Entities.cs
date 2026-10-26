using Azure;
using Azure.Data.Tables;

namespace EcoPort.Cloud.Core.Storage;

// Table Storage has no unsigned types: u32 values (seq, counters) are stored as Int64.

/// <summary>Base for all rows: the four ITableEntity members.</summary>
public abstract class EntityBase : ITableEntity
{
    public string PartitionKey { get; set; } = "";

    public string RowKey { get; set; } = "";

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }
}

/// <summary><c>Telemetry</c>: one raw <c>sample</c> record (immutable).</summary>
public sealed class TelemetryEntity : EntityBase
{
    public long Seq { get; set; }

    public DateTimeOffset T { get; set; }

    public long UptimeMs { get; set; }

    public string BootId { get; set; } = "";

    public bool Simulated { get; set; }

    public int OpState { get; set; }

    public int ElectricityRateW { get; set; }

    public int ElectricityCumulativeWh { get; set; }

    public int TotalStorageCapacityWh { get; set; }

    public int PresentEnergyTakeWh { get; set; }

    public bool? LinkOk { get; set; }

    public long? LinkRetries { get; set; }

    public long? LinkNaks { get; set; }
}

/// <summary><c>DeviceEvents</c>: one raw <c>event</c> or <c>health</c> record (immutable).</summary>
public sealed class DeviceEventEntity : EntityBase
{
    public long Seq { get; set; }

    /// <summary><c>event</c> or <c>health</c>.</summary>
    public string Kind { get; set; } = "";

    public DateTimeOffset T { get; set; }

    public long UptimeMs { get; set; }

    public string BootId { get; set; } = "";

    public bool Simulated { get; set; }

    // event
    public string? Type { get; set; }

    /// <summary>The event's <c>data</c> object as compact JSON.</summary>
    public string? DataJson { get; set; }

    // health
    public int? RssiDbm { get; set; }

    public long? FreeHeapBytes { get; set; }

    public long? BufferDepth { get; set; }

    public string? ResetReason { get; set; }
}

/// <summary><c>CurrentState</c>: latest raw values per device (PK <c>DEVICE</c>, RK device ID).</summary>
public sealed class CurrentStateEntity : EntityBase
{
    /// <summary>Server time of the last authenticated, valid upload.</summary>
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>Highest seq applied to this row.</summary>
    public long LastSeq { get; set; }

    public string BootId { get; set; } = "";

    public string Firmware { get; set; } = "";

    public bool Simulated { get; set; }

    /// <summary>Seq of the sample the values below came from; null until the first sample.</summary>
    public long? SampleSeq { get; set; }

    public DateTimeOffset? SampleTime { get; set; }

    public int? OpState { get; set; }

    public int? ElectricityRateW { get; set; }

    public int? TotalStorageCapacityWh { get; set; }

    public int? PresentEnergyTakeWh { get; set; }
}

/// <summary>
/// <c>Devices</c>: registry row (PK <c>DEVICE</c>, RK device ID). Written by provisioning; other
/// columns (e.g. <c>CreatedAt</c>) are not read in milestone 1.
/// </summary>
public sealed class DeviceEntity : EntityBase
{
}
