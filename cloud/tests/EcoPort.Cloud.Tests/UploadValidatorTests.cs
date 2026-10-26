using EcoPort.Cloud.Contracts;
using EcoPort.Cloud.Core.Ingest;
using static EcoPort.Cloud.Tests.Requests;

namespace EcoPort.Cloud.Tests;

public class UploadValidatorTests
{
    private static string? Validate(UploadRequest req, string header = "WH001") =>
        UploadValidator.Validate(req, header, Now);

    private static UploadRequest ReplaceRecord(UploadRequest req, int index, DeviceRecord record)
    {
        var records = req.Records.ToList();
        records[index] = record;
        return req with { Records = records };
    }

    [Fact]
    public void ContractExample_IsValid() => Assert.Null(Validate(Example()));

    [Fact]
    public void ZeroRecords_Rejected() => Assert.NotNull(Validate(Upload()));

    [Fact]
    public void HundredRecords_Accepted()
    {
        var records = Enumerable.Range(0, 100).Select(i => Sample((uint)i, "2026-10-09T20:30:00.120Z")).ToArray();
        Assert.Null(Validate(Upload(records)));
    }

    [Fact]
    public void HundredAndOneRecords_Rejected()
    {
        var records = Enumerable.Range(0, 101).Select(i => Sample((uint)i, "2026-10-09T20:30:00.120Z")).ToArray();
        Assert.NotNull(Validate(Upload(records)));
    }

    [Fact]
    public void NonIncreasingSeq_Rejected()
    {
        Assert.NotNull(Validate(Upload(Sample(5, "2026-10-09T20:30:00Z"), Sample(5, "2026-10-09T20:30:15Z"))));
        Assert.NotNull(Validate(Upload(Sample(6, "2026-10-09T20:30:00Z"), Sample(5, "2026-10-09T20:30:15Z"))));
    }

    [Fact]
    public void UnknownKind_Rejected()
    {
        var req = ReplaceRecord(Example(), 0, Example().Records[0] with { Kind = "telemetry" });
        Assert.NotNull(Validate(req));
    }

    [Fact]
    public void SampleMissingOpState_Rejected()
    {
        var req = ReplaceRecord(Example(), 1, Example().Records[1] with { OpState = null });
        Assert.NotNull(Validate(req));
    }

    [Fact]
    public void SampleMissingCommodity_Rejected()
    {
        var req = ReplaceRecord(Example(), 1, Example().Records[1] with { Commodity = null });
        Assert.NotNull(Validate(req));
    }

    [Fact]
    public void EventMissingType_Rejected()
    {
        var req = ReplaceRecord(Example(), 0, Example().Records[0] with { Type = null });
        Assert.NotNull(Validate(req));
        var blank = ReplaceRecord(Example(), 0, Example().Records[0] with { Type = "" });
        Assert.NotNull(Validate(blank));
    }

    [Fact]
    public void MissingT_Rejected()
    {
        var req = ReplaceRecord(Example(), 2, Example().Records[2] with { T = null });
        Assert.NotNull(Validate(req));
    }

    [Fact]
    public void TMoreThan5MinInFuture_Rejected()
    {
        var future = Now + TimeSpan.FromMinutes(5) + TimeSpan.FromMilliseconds(1);
        var req = Upload(Sample(1, future.ToString("O")));
        Assert.NotNull(Validate(req));
        Assert.Null(Validate(Upload(Sample(1, (Now + TimeSpan.FromMinutes(5)).ToString("O")))));
    }

    [Fact]
    public void TOlderThan30Days_Rejected()
    {
        var old = Now - TimeSpan.FromDays(30) - TimeSpan.FromMilliseconds(1);
        Assert.NotNull(Validate(Upload(Sample(1, old.ToString("O")))));
        Assert.Null(Validate(Upload(Sample(1, (Now - TimeSpan.FromDays(30)).ToString("O")))));
    }

    [Fact]
    public void BodyDeviceIdDifferentFromHeader_Rejected()
    {
        Assert.NotNull(Validate(Example(), header: "WH002"));
        Assert.NotNull(Validate(Example() with { DeviceId = "wh001" }));
    }

    [Fact]
    public void NullRecordsArray_Rejected()
    {
        Assert.NotNull(Validate(Example() with { Records = null! }));
    }

    [Fact]
    public void Messages_AreReadable()
    {
        var message = Validate(Upload(Sample(6, "2026-10-09T20:30:00Z"), Sample(5, "2026-10-09T20:30:15Z")));
        Assert.Contains("seq", message);
    }
}
