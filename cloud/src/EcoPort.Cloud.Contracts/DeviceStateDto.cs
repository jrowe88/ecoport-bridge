namespace EcoPort.Cloud.Contracts;

/// <summary><c>GET /api/devices/{deviceId}/state</c>: latest raw values plus read-time derived values.</summary>
/// <param name="Connectivity"><c>online</c>, <c>delayed</c> or <c>offline</c>.</param>
/// <param name="StoredEnergyPercent">0–100, or null when capacity is 0 (vacation) or unknown.</param>
public sealed record DeviceStateDto(
    string DeviceId,
    DateTimeOffset? LastSeen,
    string Connectivity,
    bool Simulated,
    DateTimeOffset? SampleTime,
    int? OpState,
    int? ElectricityRateW,
    int? TotalStorageCapacityWh,
    int? PresentEnergyTakeWh,
    double? StoredEnergyPercent);
