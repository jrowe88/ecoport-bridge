namespace EcoPort.Cloud.Contracts;

/// <summary>One entry of <c>GET /api/devices</c>.</summary>
/// <param name="Connectivity"><c>online</c>, <c>delayed</c> or <c>offline</c>.</param>
public sealed record DeviceSummaryDto(
    string DeviceId,
    DateTimeOffset? LastSeen,
    string Connectivity,
    bool Simulated);
