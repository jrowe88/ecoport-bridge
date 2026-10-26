using System.Net;
using System.Text;
using EcoPort.Web.Services;

namespace EcoPort.Web.Tests;

public sealed class EcoPortApiClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (EcoPortApiClient Client, StubHandler Handler) Create(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://dash.example/") };
        return (new EcoPortApiClient(http), handler);
    }

    [Fact]
    public async Task GetDevices_ReadsCamelCaseList()
    {
        var (client, handler) = Create(HttpStatusCode.OK,
            """[{"deviceId":"WH001","lastSeen":"2026-10-09T20:31:02Z","connectivity":"online","simulated":true}]""");

        var devices = await client.GetDevicesAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.Equal("https://dash.example/api/devices", handler.Requests.Single().AbsoluteUri);
        var d = Assert.Single(devices);
        Assert.Equal("WH001", d.DeviceId);
        Assert.True(d.Simulated);
    }

    [Fact]
    public async Task GetState_404_ReturnsNull()
    {
        var (client, handler) = Create(HttpStatusCode.NotFound, "");

        var state = await client.GetStateAsync("WH 1", Xunit.TestContext.Current.CancellationToken);

        Assert.Null(state);
        Assert.Equal("/api/devices/WH%201/state", handler.Requests.Single().AbsolutePath);
    }

    [Fact]
    public async Task GetState_ServerError_Throws()
    {
        var (client, _) = Create(HttpStatusCode.InternalServerError, "");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetStateAsync("WH001", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetTelemetry_SendsUtcIsoRange()
    {
        var (client, handler) = Create(HttpStatusCode.OK,
            """[{"t":"2026-10-09T20:30:00.120Z","opState":1,"electricityRateW":242,"storedEnergyPercent":65.02}]""");
        var to = new DateTimeOffset(2026, 10, 9, 15, 30, 0, TimeSpan.FromHours(-5));

        var points = await client.GetTelemetryAsync("WH001", to.AddHours(-24), to, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(
            "/api/devices/WH001/telemetry?from=2026-10-08T20:30:00Z&to=2026-10-09T20:30:00Z",
            handler.Requests.Single().PathAndQuery);
        Assert.Equal(242, Assert.Single(points).ElectricityRateW);
    }
}
