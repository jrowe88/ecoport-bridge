using System.Text.Json;
using EcoPort.Cloud.Contracts;

namespace EcoPort.Cloud.Tests;

public class ContractTests
{
    [Fact]
    public void UploadRequestExample_RoundTrips()
    {
        var bytes = ContractFiles.Bytes("upload-request.example.json");
        var req = JsonSerializer.Deserialize<UploadRequest>(bytes, JsonDefaults.Options)!;

        var json = JsonSerializer.Serialize(req, JsonDefaults.Options);
        using var original = JsonDocument.Parse(bytes);
        using var again = JsonDocument.Parse(json);

        Assert.Equal("WH001", again.RootElement.GetProperty("deviceId").GetString());
        var a = original.RootElement.GetProperty("records").EnumerateArray().ToList();
        var b = again.RootElement.GetProperty("records").EnumerateArray().ToList();
        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].GetProperty("seq").GetUInt32(), b[i].GetProperty("seq").GetUInt32());
            Assert.Equal(a[i].GetProperty("kind").GetString(), b[i].GetProperty("kind").GetString());
            Assert.Equal(
                a[i].GetProperty("t").GetDateTimeOffset(),
                b[i].GetProperty("t").GetDateTimeOffset());
        }
    }

    [Fact]
    public void UploadRequestExample_KindSpecificFieldsDeserialize()
    {
        var req = JsonSerializer.Deserialize<UploadRequest>(
            ContractFiles.Bytes("upload-request.example.json"), JsonDefaults.Options)!;

        Assert.Equal("c4e633a2", req.BootId);
        Assert.True(req.Simulated);
        var sample = req.Records.Single(r => r.Kind == "sample");
        Assert.Equal(1, sample.OpState);
        Assert.Equal(12011, sample.Commodity!.TotalStorageCapacityWh);
        Assert.Equal(4202, sample.Commodity.PresentEnergyTakeWh);
        Assert.True(sample.Link!.Ok);
        var boot = req.Records.First(r => r.Kind == "event");
        Assert.Equal("boot", boot.Type);
        Assert.Equal("PowerOn", boot.Data!.Value.GetProperty("resetReason").GetString());
        var health = req.Records.Single(r => r.Kind == "health");
        Assert.Equal(-58, health.RssiDbm);
        Assert.Equal(182344u, health.FreeHeapBytes);
    }

    [Fact]
    public void UploadResponseExample_Deserializes_AndSerializesCamelCase()
    {
        var resp = JsonSerializer.Deserialize<UploadResponse>(
            ContractFiles.Bytes("upload-response.example.json"), JsonDefaults.Options)!;
        Assert.Equal(12786u, resp.AckSeq);
        Assert.False(resp.CommandsPending);

        var json = JsonSerializer.Serialize(resp, JsonDefaults.Options);
        Assert.Contains("\"ackSeq\":12786", json);
        Assert.Contains("\"commandsPending\":false", json);
        Assert.Contains("\"serverTime\":", json);
    }

    [Fact]
    public void MalformedRecord_FailsToDeserialize()
    {
        // seq is a u32: a negative value is a schema error, not silently 0.
        const string body = """{"deviceId":"WH001","bootId":"c4e633a2","firmware":"x","simulated":true,"records":[{"seq":-1,"kind":"sample","uptimeMs":1,"t":"2026-10-09T20:30:00.120Z"}]}""";
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<UploadRequest>(body, JsonDefaults.Options));
    }

    [Fact]
    public void MissingRequiredTopLevelField_FailsToDeserialize()
    {
        const string body = """{"deviceId":"WH001","firmware":"x","simulated":true,"records":[]}""";
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<UploadRequest>(body, JsonDefaults.Options));
    }
}
