using EcoPort.Cloud.Contracts;
using EcoPort.Web.Pages;
using EcoPort.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;

namespace EcoPort.Web.Tests;

public sealed class DevicePageTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 20, 31, 0, TimeSpan.Zero);
    private readonly FakeEcoPortApi _api = new();
    private readonly FakeTimeProvider _time = new(Now);

    public DevicePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton<IEcoPortApi>(_api);
    }

    private static TelemetryPointDto Point(int minutesAgo, int watts, double? percent) =>
        new(Now.AddMinutes(-minutesAgo), OpState: 1, ElectricityRateW: watts, StoredEnergyPercent: percent);

    private static DeviceStateDto State(
        double? storedEnergyPercent = 65.0,
        string connectivity = "online",
        bool simulated = false,
        int? opState = 1) =>
        new(
            DeviceId: "WH001",
            LastSeen: Now.AddSeconds(-20),
            Connectivity: connectivity,
            Simulated: simulated,
            SampleTime: Now.AddSeconds(-25),
            OpState: opState,
            ElectricityRateW: 242,
            TotalStorageCapacityWh: 12011,
            PresentEnergyTakeWh: 4202,
            StoredEnergyPercent: storedEnergyPercent);

    private IRenderedComponent<Device> RenderDevice(DeviceStateDto state)
    {
        _api.States[state.DeviceId] = state;
        return Render<Device>(p => p.Add(x => x.DeviceId, state.DeviceId));
    }

    [Fact]
    public void StoredEnergy_Renders_Percent()
    {
        var cut = RenderDevice(State(storedEnergyPercent: 65.0));

        cut.WaitForAssertion(() =>
            Assert.Equal("65%", cut.Find("[data-testid=stored-energy]").TextContent.Trim()));
        Assert.DoesNotContain("vacation or no data", cut.Markup);
    }

    [Fact]
    public void StoredEnergy_Null_RendersDash_AndVacationHint()
    {
        var cut = RenderDevice(State(storedEnergyPercent: null));

        cut.WaitForAssertion(() =>
            Assert.Equal("—", cut.Find("[data-testid=stored-energy]").TextContent.Trim()));
        Assert.Contains("vacation or no data", cut.Markup);
    }

    [Fact]
    public void Simulated_RendersSimulatedChip()
    {
        var cut = RenderDevice(State(simulated: true));

        cut.WaitForAssertion(() =>
        {
            var chip = cut.Find("[data-testid=simulated-chip]");
            Assert.Contains("mud-chip", chip.ClassName);
            Assert.Equal("Simulated", chip.TextContent.Trim());
        });
    }

    [Fact]
    public void NotSimulated_HasNoSimulatedChip()
    {
        var cut = RenderDevice(State(simulated: false));

        cut.WaitForAssertion(() => cut.Find("[data-testid=stored-energy]"));
        Assert.Empty(cut.FindAll("[data-testid=simulated-chip]"));
    }

    [Fact]
    public void Offline_RendersErrorColoredConnectivityChip()
    {
        var cut = RenderDevice(State(connectivity: "offline"));

        cut.WaitForAssertion(() =>
        {
            var chip = cut.Find("[data-testid=connectivity-chip]");
            Assert.Contains("mud-chip-color-error", chip.ClassName);
            Assert.Contains("offline", chip.TextContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(0, "Idle")]
    [InlineData(1, "Running")]
    [InlineData(7, "7")]
    public void OpState_IsLabelled(int opState, string expected)
    {
        var cut = RenderDevice(State(opState: opState));

        cut.WaitForAssertion(() =>
            Assert.Equal(expected, cut.Find("[data-testid=opstate]").TextContent.Trim()));
    }

    [Fact]
    public void Power_RendersWatts()
    {
        var cut = RenderDevice(State());

        cut.WaitForAssertion(() =>
            Assert.Equal("242 W", cut.Find("[data-testid=power]").TextContent.Trim()));
    }

    [Fact]
    public void Telemetry_IsRequestedForTheLast24Hours()
    {
        var cut = RenderDevice(State());

        cut.WaitForAssertion(() => Assert.Single(_api.TelemetryRequests));
        var (deviceId, from, to) = _api.TelemetryRequests[0];
        Assert.Equal("WH001", deviceId);
        Assert.Equal(TimeSpan.FromHours(24), to - from);
    }

    [Fact]
    public void Telemetry_RendersPowerAndStoredEnergyCharts()
    {
        _api.Telemetry.AddRange([Point(30, 240, 64.0), Point(15, 4500, null), Point(0, 242, 65.0)]);

        var cut = RenderDevice(State());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=power-chart] svg")));
        Assert.NotEmpty(cut.FindAll("[data-testid=energy-chart] svg"));
        Assert.Empty(cut.FindAll("[data-testid=no-telemetry]"));
        Assert.Contains("Power (W)", cut.Markup);
        Assert.Contains("Stored energy (%)", cut.Markup);
    }

    [Fact]
    public void NoTelemetry_RendersEmptyMessage()
    {
        var cut = RenderDevice(State());

        cut.WaitForAssertion(() => cut.Find("[data-testid=no-telemetry]"));
        Assert.Empty(cut.FindAll("[data-testid=power-chart]"));
    }

    [Fact]
    public void Refreshes_Every30Seconds()
    {
        var cut = RenderDevice(State());
        cut.WaitForAssertion(() => Assert.Single(_api.TelemetryRequests));

        _api.States["WH001"] = State() with { ElectricityRateW = 900 };
        _time.Advance(TimeSpan.FromSeconds(30));

        cut.WaitForAssertion(() =>
            Assert.Equal("900 W", cut.Find("[data-testid=power]").TextContent.Trim()));
        Assert.Equal(2, _api.TelemetryRequests.Count);
    }

    [Fact]
    public async Task Dispose_StopsRefreshing()
    {
        var cut = RenderDevice(State());
        cut.WaitForAssertion(() => Assert.Single(_api.TelemetryRequests));

        await DisposeComponentsAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        Assert.Single(_api.TelemetryRequests);
    }

    [Fact]
    public void RefreshFailure_KeepsLastData_AndWarns()
    {
        var cut = RenderDevice(State());
        cut.WaitForAssertion(() => cut.Find("[data-testid=power]"));

        _api.Failure = new HttpRequestException("timeout");
        _time.Advance(TimeSpan.FromSeconds(30));

        cut.WaitForAssertion(() => cut.Find("[data-testid=load-error]"));
        Assert.Equal("242 W", cut.Find("[data-testid=power]").TextContent.Trim());
    }

    [Fact]
    public void UnknownDevice_RendersNotFound()
    {
        var cut = Render<Device>(p => p.Add(x => x.DeviceId, "NOPE"));

        cut.WaitForAssertion(() => Assert.Contains("not found", cut.Markup, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApiFailure_RendersErrorAlert_NotException()
    {
        _api.Failure = new HttpRequestException("connection refused");

        var cut = Render<Device>(p => p.Add(x => x.DeviceId, "WH001"));

        cut.WaitForAssertion(() => cut.Find("[data-testid=load-error]"));
    }
}
