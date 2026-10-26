namespace EcoPort.Cloud.Contracts;

/// <summary>Body of <c>POST /device/v1/records</c> (see <c>contracts/device-v1/README.md</c>).</summary>
public sealed record UploadRequest(
    string DeviceId,
    string BootId,
    string Firmware,
    bool Simulated,
    IReadOnlyList<DeviceRecord> Records);
