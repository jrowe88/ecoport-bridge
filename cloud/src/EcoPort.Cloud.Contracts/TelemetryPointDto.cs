namespace EcoPort.Cloud.Contracts;

/// <summary>One point of <c>GET /api/devices/{deviceId}/telemetry</c>.</summary>
public sealed record TelemetryPointDto(
    DateTimeOffset T,
    int OpState,
    int ElectricityRateW,
    double? StoredEnergyPercent);
