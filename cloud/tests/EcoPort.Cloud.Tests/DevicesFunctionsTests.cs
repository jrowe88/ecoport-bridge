using EcoPort.Cloud.Core.Ingest;
using EcoPort.Cloud.Core.Read;
using EcoPort.Cloud.Core.Storage;
using Microsoft.Extensions.Time.Testing;
using static EcoPort.Cloud.Tests.Requests;

namespace EcoPort.Cloud.Tests;

/// <summary>
/// The user API's behaviour. The Functions in EcoPort.Cloud.Api are thin wrappers over
/// <see cref="DeviceQueries"/>, which is tested here against <see cref="FakeTableStore"/>.
/// </summary>
public class DevicesFunctionsTests
{
    private readonly FakeTableStore _store = new();
    private readonly FakeTimeProvider _time = new(Now + TimeSpan.FromSeconds(30));
    private readonly DeviceQueries _queries;

    public DevicesFunctionsTests()
    {
        _store.AddDevice("WH001");
        _queries = new DeviceQueries(_store, _time);
    }

    private Task Ingest(params Contracts.DeviceRecord[] records) =>
        new RecordIngestor(_store).IngestAsync(Upload(records), Now, CancellationToken.None);

    [Fact]
    public async Task List_ReturnsDevicesWithConnectivity()
    {
        _store.AddDevice("WH002");
        await Ingest(Sample(1, "2026-10-09T20:30:00Z"));

        var list = await _queries.ListDevicesAsync(CancellationToken.None);

        Assert.Equal(["WH001", "WH002"], list.Select(d => d.DeviceId));
        Assert.Equal("online", list[0].Connectivity);
        Assert.Equal(Now, list[0].LastSeen);
        Assert.True(list[0].Simulated);
        Assert.Equal("offline", list[1].Connectivity);
        Assert.Null(list[1].LastSeen);
        Assert.False(list[1].Simulated);
    }

    [Fact]
    public async Task State_UnknownDevice_404()
    {
        var result = await _queries.GetStateAsync("WH999", CancellationToken.None);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task State_ReturnsRawValuesAndStoredEnergy()
    {
        await Ingest(Sample(1, "2026-10-09T20:30:00Z", opState: 1, rateW: 242, capacityWh: 12011, takeWh: 4202));
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await _queries.GetStateAsync("WH001", CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        var s = result.Value!;
        Assert.Equal("WH001", s.DeviceId);
        Assert.Equal("delayed", s.Connectivity);
        Assert.Equal(Now, s.LastSeen);
        Assert.True(s.Simulated);
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T20:30:00Z"), s.SampleTime);
        Assert.Equal(1, s.OpState);
        Assert.Equal(242, s.ElectricityRateW);
        Assert.Equal(12011, s.TotalStorageCapacityWh);
        Assert.Equal(4202, s.PresentEnergyTakeWh);
        Assert.Equal(65.02, s.StoredEnergyPercent!.Value, 2);
    }

    [Fact]
    public async Task State_ZeroCapacity_StoredEnergyIsNull()
    {
        await Ingest(Sample(1, "2026-10-09T20:30:00Z", capacityWh: 0, takeWh: 0));

        var s = (await _queries.GetStateAsync("WH001", CancellationToken.None)).Value!;

        Assert.Equal(0, s.TotalStorageCapacityWh);
        Assert.Null(s.StoredEnergyPercent);
    }

    [Fact]
    public async Task State_KnownDeviceNeverSeen_IsOfflineWithNulls()
    {
        var result = await _queries.GetStateAsync("WH001", CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("offline", result.Value!.Connectivity);
        Assert.Null(result.Value.LastSeen);
        Assert.Null(result.Value.OpState);
        Assert.Null(result.Value.StoredEnergyPercent);
    }

    [Fact]
    public async Task Telemetry_UnknownDevice_404()
    {
        var result = await _queries.GetTelemetryAsync("WH999", null, null, CancellationToken.None);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Telemetry_25HourRange_400()
    {
        var result = await _queries.GetTelemetryAsync(
            "WH001", "2026-10-08T00:00:00Z", "2026-10-09T01:00:00Z", CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Theory]
    [InlineData("yesterday", null)]
    [InlineData(null, "not-a-date")]
    [InlineData("2026-10-09T12:00:00Z", "2026-10-09T11:00:00Z")] // from after to
    public async Task Telemetry_BadRange_400(string? from, string? to)
    {
        var result = await _queries.GetTelemetryAsync("WH001", from, to, CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Telemetry_ExactlyOneDay_IsAllowed()
    {
        var result = await _queries.GetTelemetryAsync(
            "WH001", "2026-10-08T20:00:00Z", "2026-10-09T20:00:00Z", CancellationToken.None);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task Telemetry_DefaultsToLast24Hours_AndMapsPoints()
    {
        await Ingest(
            Sample(1, "2026-10-08T20:00:00Z", takeWh: 0), // just over 24 h before "now": excluded
            Sample(2, "2026-10-09T20:00:00Z", opState: 1, rateW: 242, capacityWh: 12011, takeWh: 4202),
            Sample(3, "2026-10-09T20:00:15Z", opState: 0, rateW: 5, capacityWh: 0, takeWh: 0));

        var result = await _queries.GetTelemetryAsync("WH001", null, null, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        var points = result.Value!;
        Assert.Equal(2, points.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T20:00:00Z"), points[0].T);
        Assert.Equal(242, points[0].ElectricityRateW);
        Assert.Equal(65.02, points[0].StoredEnergyPercent!.Value, 2);
        Assert.Equal(0, points[1].OpState);
        Assert.Null(points[1].StoredEnergyPercent);
    }

    [Fact]
    public async Task Telemetry_FullDay_IsDownsampledTo500()
    {
        var start = _time.GetUtcNow() - TimeSpan.FromHours(24);
        for (var batch = 0; batch < 5760 / 100; batch++)
        {
            var records = Enumerable.Range(0, 100)
                .Select(i => batch * 100 + i)
                .Select(i => Sample((uint)(i + 1), start.AddSeconds(15 * i + 1).ToString("O")))
                .ToArray();
            await new RecordIngestor(_store).IngestAsync(Upload(records), _time.GetUtcNow(), CancellationToken.None);
        }

        var result = await _queries.GetTelemetryAsync("WH001", null, null, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(DeviceQueries.MaxTelemetryPoints, result.Value!.Count);
    }
}
