using System.Text.Json;
using EcoPort.Cloud.Contracts;

namespace EcoPort.Cloud.Tests;

/// <summary>Builders for upload requests used across the ingest tests.</summary>
internal static class Requests
{
    /// <summary>Server clock matching the contract example (X-Timestamp of the signing vector).</summary>
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-09T20:31:02Z");

    public static UploadRequest Example() =>
        JsonSerializer.Deserialize<UploadRequest>(ContractFiles.Bytes("upload-request.example.json"), JsonDefaults.Options)!;

    public static DeviceRecord Sample(
        uint seq, string t, int opState = 1, int rateW = 242, int capacityWh = 12011, int takeWh = 4202) =>
        new(seq, RecordKinds.Sample, 1000 + seq, DateTimeOffset.Parse(t),
            OpState: opState,
            Commodity: new CommodityDto(rateW, 0, capacityWh, takeWh),
            Link: new LinkDto(true, 0, 0));

    public static DeviceRecord Event(uint seq, string t, string type = "opStateChanged") =>
        new(seq, RecordKinds.Event, 1000 + seq, DateTimeOffset.Parse(t),
            Type: type,
            Data: JsonDocument.Parse("""{"from":1,"to":0}""").RootElement.Clone());

    public static DeviceRecord Health(uint seq, string t) =>
        new(seq, RecordKinds.Health, 1000 + seq, DateTimeOffset.Parse(t),
            RssiDbm: -58, FreeHeapBytes: 182344, BufferDepth: 4, ResetReason: "PowerOn");

    public static UploadRequest Upload(params DeviceRecord[] records) =>
        new("WH001", "c4e633a2", "0.2.0", true, records);

    public static UploadRequest WithBoot(this UploadRequest req, string bootId) => req with { BootId = bootId };
}
