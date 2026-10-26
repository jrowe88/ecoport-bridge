using EcoPort.Cloud.Contracts;
using EcoPort.Web.Services;

namespace EcoPort.Web.Tests;

/// <summary>In-memory <see cref="IEcoPortApi"/>; set a property to an exception to make that call fail.</summary>
internal sealed class FakeEcoPortApi : IEcoPortApi
{
    public List<DeviceSummaryDto> Devices { get; } = [];
    public Dictionary<string, DeviceStateDto> States { get; } = [];
    public List<TelemetryPointDto> Telemetry { get; } = [];
    public Exception? Failure { get; set; }
    public List<(string DeviceId, DateTimeOffset From, DateTimeOffset To)> TelemetryRequests { get; } = [];

    public Task<IReadOnlyList<DeviceSummaryDto>> GetDevicesAsync(CancellationToken ct = default)
    {
        ThrowIfFailing();
        return Task.FromResult<IReadOnlyList<DeviceSummaryDto>>(Devices.ToList());
    }

    public Task<DeviceStateDto?> GetStateAsync(string deviceId, CancellationToken ct = default)
    {
        ThrowIfFailing();
        return Task.FromResult(States.GetValueOrDefault(deviceId));
    }

    public Task<IReadOnlyList<TelemetryPointDto>> GetTelemetryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        ThrowIfFailing();
        TelemetryRequests.Add((deviceId, from, to));
        return Task.FromResult<IReadOnlyList<TelemetryPointDto>>(Telemetry.ToList());
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
