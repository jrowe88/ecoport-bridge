using System.Text;
using System.Text.Json;
using EcoPort.Cloud.Contracts;
using EcoPort.Cloud.Core.Auth;
using EcoPort.Cloud.Core.Ingest;
using EcoPort.Cloud.Core.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace EcoPort.Cloud.Tests;

public class DeviceUploadHandlerTests
{
    private const string SecretB64 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string Path = "/device/v1/records";
    private const string Timestamp = "2026-10-09T20:31:02Z";

    private readonly FakeTableStore _store = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-09T20:31:03Z"));
    private readonly DeviceUploadHandler _handler;

    public DeviceUploadHandlerTests()
    {
        _store.AddDevice("WH001");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceSecrets:WH001"] = SecretB64 })
            .Build();
        _handler = new DeviceUploadHandler(_store, new ConfigDeviceSecretStore(config), new RecordIngestor(_store), _time);
    }

    private static byte[] ExampleBody => ContractFiles.Bytes("upload-request.example.json");

    private static string Sign(byte[] body, string timestamp = Timestamp) =>
        DeviceRequestSigner.ComputeSignature(Convert.FromBase64String(SecretB64), "POST", Path, timestamp, body);

    private Task<DeviceApiResult> Post(byte[] body, string? deviceId = "WH001", string? timestamp = Timestamp, string? signature = null) =>
        _handler.HandleAsync("POST", Path, deviceId, timestamp, signature ?? Sign(body, timestamp ?? ""), body, CancellationToken.None);

    [Fact]
    public async Task SignedContractExample_Returns200WithAck()
    {
        var result = await Post(ExampleBody);

        Assert.Equal(200, result.StatusCode);
        var resp = JsonSerializer.Deserialize<UploadResponse>(result.Json!, JsonDefaults.Options)!;
        Assert.Equal(12786u, resp.AckSeq);
        Assert.False(resp.CommandsPending);
        Assert.Single(_store.Rows<TelemetryEntity>(TableNames.Telemetry));
    }

    [Fact]
    public async Task SigningVectorSignature_IsAccepted()
    {
        // The exact signature the firmware's reference vector produces.
        var result = await Post(ExampleBody, signature: "qBvw4dYogQ4rhmZl9a3UT7TriIsI+nIaGCSJVPM2nEA=");
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task UnknownDevice_Returns401WithEmptyBody()
    {
        var body = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ExampleBody).Replace("WH001", "WH002"));
        var result = await Post(body, deviceId: "WH002");
        Assert.Equal(401, result.StatusCode);
        Assert.Null(result.Json);
    }

    [Fact]
    public async Task KnownDeviceWithoutSecret_Returns401()
    {
        _store.AddDevice("WH009");
        var result = await Post(ExampleBody, deviceId: "WH009");
        Assert.Equal(401, result.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("WH 001")]
    [InlineData("WH001' or ''='")]
    public async Task MissingOrInvalidDeviceIdHeader_Returns401(string? deviceId)
    {
        var result = await Post(ExampleBody, deviceId: deviceId);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task WrongSecret_Returns401_AndStoresNothing()
    {
        var wrong = DeviceRequestSigner.ComputeSignature(new byte[32], "POST", Path, Timestamp, ExampleBody);
        var result = await Post(ExampleBody, signature: wrong);
        Assert.Equal(401, result.StatusCode);
        Assert.Null(result.Json);
        Assert.Empty(_store.Rows<TelemetryEntity>(TableNames.Telemetry));
    }

    [Fact]
    public async Task ClockSkew_Returns401()
    {
        _time.Advance(TimeSpan.FromMinutes(6));
        var result = await Post(ExampleBody);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task BodyOver64KiB_Returns413()
    {
        var body = new byte[DeviceUploadHandler.MaxBodyBytes + 1];
        var result = await Post(body);
        Assert.Equal(413, result.StatusCode);
        Assert.Null(result.Json);
    }

    [Fact]
    public async Task InvalidJson_Returns400WithError()
    {
        var result = await Post("{not json"u8.ToArray());
        Assert.Equal(400, result.StatusCode);
        using var doc = JsonDocument.Parse(result.Json!);
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task JsonNull_Returns400()
    {
        var result = await Post("null"u8.ToArray());
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task ValidationFailure_Returns400WithError()
    {
        var body = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ExampleBody).Replace("\"kind\": \"health\"", "\"kind\": \"bogus\""));
        var result = await Post(body);
        Assert.Equal(400, result.StatusCode);
        using var doc = JsonDocument.Parse(result.Json!);
        Assert.Contains("kind", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task BodyDeviceIdMismatch_Returns400()
    {
        _store.AddDevice("WH002");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceSecrets:WH002"] = SecretB64 })
            .Build();
        var handler = new DeviceUploadHandler(_store, new ConfigDeviceSecretStore(config), new RecordIngestor(_store), _time);

        var result = await handler.HandleAsync("POST", Path, "WH002", Timestamp, Sign(ExampleBody), ExampleBody, CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }
}
