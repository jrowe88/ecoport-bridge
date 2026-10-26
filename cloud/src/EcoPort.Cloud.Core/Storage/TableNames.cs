namespace EcoPort.Cloud.Core.Storage;

/// <summary>Table names (milestone 1). Must match the Bicep storage module.</summary>
public static class TableNames
{
    public const string Devices = "Devices";
    public const string Telemetry = "Telemetry";
    public const string DeviceEvents = "DeviceEvents";
    public const string CurrentState = "CurrentState";

    public static IReadOnlyList<string> All { get; } = [Devices, Telemetry, DeviceEvents, CurrentState];
}
