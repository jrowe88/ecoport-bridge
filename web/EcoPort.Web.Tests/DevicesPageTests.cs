using EcoPort.Cloud.Contracts;
using EcoPort.Web.Pages;
using EcoPort.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace EcoPort.Web.Tests;

public sealed class DevicesPageTests : BunitContext
{
    private readonly FakeEcoPortApi _api = new();

    public DevicesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<IEcoPortApi>(_api);
    }

    [Fact]
    public void ListsDevices_WithLinksAndConnectivityChips()
    {
        _api.Devices.Add(new DeviceSummaryDto("WH001", DateTimeOffset.UtcNow, "online", Simulated: true));
        _api.Devices.Add(new DeviceSummaryDto("WH002", null, "offline", Simulated: false));

        var cut = Render<Devices>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=connectivity-chip]").Count));
        Assert.NotNull(cut.Find("a[href='devices/WH001']"));
        Assert.NotNull(cut.Find("a[href='devices/WH002']"));
        var chips = cut.FindAll("[data-testid=connectivity-chip]");
        Assert.Contains("mud-chip-color-success", chips[0].ClassName);
        Assert.Contains("mud-chip-color-error", chips[1].ClassName);
    }

    [Fact]
    public void NoDevices_RendersEmptyState()
    {
        var cut = Render<Devices>();

        cut.WaitForAssertion(() => Assert.Contains("No devices", cut.Markup));
    }

    [Fact]
    public void ApiFailure_RendersErrorAlert_NotException()
    {
        _api.Failure = new HttpRequestException("connection refused");

        var cut = Render<Devices>();

        cut.WaitForAssertion(() => cut.Find("[data-testid=load-error]"));
    }
}
