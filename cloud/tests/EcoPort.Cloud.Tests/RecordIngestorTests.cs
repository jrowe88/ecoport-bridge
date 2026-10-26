using EcoPort.Cloud.Core.Ingest;
using EcoPort.Cloud.Core.Storage;
using static EcoPort.Cloud.Tests.Requests;

namespace EcoPort.Cloud.Tests;

public class RecordIngestorTests
{
    private readonly FakeTableStore _store = new();
    private readonly RecordIngestor _ingestor;

    public RecordIngestorTests() => _ingestor = new RecordIngestor(_store);

    [Fact]
    public async Task Samples_GoToTelemetry_EventsAndHealthToDeviceEvents()
    {
        await _ingestor.IngestAsync(Example(), Now, CancellationToken.None);

        var telemetry = _store.Rows<TelemetryEntity>(TableNames.Telemetry);
        var sample = Assert.Single(telemetry);
        Assert.Equal("WH001_20261009", sample.PartitionKey);
        Assert.Equal("203000120_0000012784", sample.RowKey);
        Assert.Equal(12784, sample.Seq);
        Assert.Equal(1, sample.OpState);
        Assert.Equal(242, sample.ElectricityRateW);
        Assert.Equal(12011, sample.TotalStorageCapacityWh);
        Assert.Equal(4202, sample.PresentEnergyTakeWh);
        Assert.Equal(0, sample.ElectricityCumulativeWh);
        Assert.True(sample.LinkOk);
        Assert.Equal("c4e633a2", sample.BootId);
        Assert.True(sample.Simulated);
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T20:30:00.120Z"), sample.T);

        var events = _store.Rows<DeviceEventEntity>(TableNames.DeviceEvents);
        Assert.Equal(["event", "event", "health"], events.Select(e => e.Kind));
        Assert.All(events, e => Assert.Equal("WH001", e.PartitionKey));
        Assert.Equal("20261009T202958320_0000012783", events[0].RowKey);
        Assert.Equal("boot", events[0].Type);
        Assert.Equal("""{"resetReason":"PowerOn"}""", events[0].DataJson);
        Assert.Equal("opStateChanged", events[1].Type);
        Assert.Equal(-58, events[2].RssiDbm);
        Assert.Equal(182344, events[2].FreeHeapBytes);
        Assert.Equal(4, events[2].BufferDepth);
        Assert.Equal("PowerOn", events[2].ResetReason);
    }

    [Fact]
    public async Task AckSeq_IsMaxSeq_AndNoCommandsPending()
    {
        var resp = await _ingestor.IngestAsync(Example(), Now, CancellationToken.None);
        Assert.Equal(12786u, resp.AckSeq);
        Assert.False(resp.CommandsPending);
        Assert.Equal(Now, resp.ServerTime);
    }

    [Fact]
    public async Task Ingest_SameRequestTwice_IsIdempotent()
    {
        var first = await _ingestor.IngestAsync(Example(), Now, CancellationToken.None);
        var telemetry = _store.Snapshot(TableNames.Telemetry);
        var events = _store.Snapshot(TableNames.DeviceEvents);
        var state = _store.Snapshot(TableNames.CurrentState);
        var writes = _store.CurrentStateWrites;

        var second = await _ingestor.IngestAsync(Example(), Now, CancellationToken.None);

        Assert.Equal(first.AckSeq, second.AckSeq);
        Assert.Equal(telemetry, _store.Snapshot(TableNames.Telemetry));
        Assert.Equal(events, _store.Snapshot(TableNames.DeviceEvents));
        Assert.Equal(state, _store.Snapshot(TableNames.CurrentState));
        Assert.Equal(writes, _store.CurrentStateWrites);
    }

    [Fact]
    public async Task Ingest_RetryLater_OnlyMovesLastSeenForward()
    {
        await _ingestor.IngestAsync(Example(), Now, CancellationToken.None);
        var before = _store.CurrentState("WH001")!;

        var later = Now + TimeSpan.FromSeconds(30);
        await _ingestor.IngestAsync(Example(), later, CancellationToken.None);
        var after = _store.CurrentState("WH001")!;

        Assert.Equal(later, after.LastSeen);
        Assert.Equal(before.LastSeq, after.LastSeq);
        Assert.Equal(before.SampleSeq, after.SampleSeq);
        Assert.Equal(before.PresentEnergyTakeWh, after.PresentEnergyTakeWh);
    }

    [Fact]
    public async Task Ingest_RecordsSpanningMidnight_WritesTwoPartitions()
    {
        var req = Upload(Sample(10, "2026-10-09T23:59:50.000Z"), Sample(11, "2026-10-10T00:00:05.000Z"));
        await _ingestor.IngestAsync(req, DateTimeOffset.Parse("2026-10-10T00:00:30Z"), CancellationToken.None);

        var batches = _store.Batches.Where(b => b.Table == TableNames.Telemetry).ToList();
        Assert.Equal(2, batches.Count);
        Assert.Equal(["WH001_20261009", "WH001_20261010"], batches.Select(b => b.Pk).Order(StringComparer.Ordinal));
        Assert.All(batches, b => Assert.Equal(1, b.Count));
    }

    [Fact]
    public async Task HundredSamples_OnePartition_OneBatch()
    {
        var start = DateTimeOffset.Parse("2026-10-09T10:00:00Z");
        var records = Enumerable.Range(0, 100)
            .Select(i => Sample((uint)i, start.AddSeconds(15 * i).ToString("O")))
            .ToArray();
        await _ingestor.IngestAsync(Upload(records), start.AddMinutes(30), CancellationToken.None);

        var batch = Assert.Single(_store.Batches);
        Assert.Equal(100, batch.Count);
    }

    [Fact]
    public async Task CurrentState_TakesLatestSampleRawValues_AndLastSeenIsNow()
    {
        var req = Upload(
            Sample(20, "2026-10-09T20:29:00Z", opState: 1, rateW: 242, takeWh: 4202),
            Event(21, "2026-10-09T20:29:10Z"),
            Sample(22, "2026-10-09T20:29:15Z", opState: 0, rateW: 5, takeWh: 4100),
            Health(23, "2026-10-09T20:29:20Z"));

        await _ingestor.IngestAsync(req, Now, CancellationToken.None);

        var state = _store.CurrentState("WH001")!;
        Assert.Equal(TableKeys.DevicePartition, state.PartitionKey);
        Assert.Equal("WH001", state.RowKey);
        Assert.Equal(Now, state.LastSeen);
        Assert.Equal(23, state.LastSeq);
        Assert.Equal(22, state.SampleSeq);
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T20:29:15Z"), state.SampleTime);
        Assert.Equal(0, state.OpState);
        Assert.Equal(5, state.ElectricityRateW);
        Assert.Equal(12011, state.TotalStorageCapacityWh);
        Assert.Equal(4100, state.PresentEnergyTakeWh);
        Assert.Equal("c4e633a2", state.BootId);
        Assert.Equal("0.2.0", state.Firmware);
        Assert.True(state.Simulated);
    }

    [Fact]
    public async Task Ingest_OlderSampleTime_DoesNotRegressCurrentState()
    {
        await _ingestor.IngestAsync(Upload(Sample(50, "2026-10-09T20:30:00Z", takeWh: 3000)), Now, CancellationToken.None);

        var later = Now + TimeSpan.FromSeconds(10);
        await _ingestor.IngestAsync(Upload(Sample(40, "2026-10-09T20:29:00Z", takeWh: 4000)), later, CancellationToken.None);

        var state = _store.CurrentState("WH001")!;
        Assert.Equal(50, state.LastSeq);
        Assert.Equal(50, state.SampleSeq);
        Assert.Equal(3000, state.PresentEnergyTakeWh);
        Assert.Equal(later, state.LastSeen);

        // The older record is still stored.
        Assert.Equal(2, _store.Rows<TelemetryEntity>(TableNames.Telemetry).Count);
    }

    [Fact]
    public async Task Ingest_EventOnly_KeepsSampleValues_UpdatesLastSeq()
    {
        await _ingestor.IngestAsync(Upload(Sample(50, "2026-10-09T20:30:00Z", takeWh: 3000)), Now, CancellationToken.None);
        await _ingestor.IngestAsync(Upload(Event(51, "2026-10-09T20:30:10Z")), Now, CancellationToken.None);

        var state = _store.CurrentState("WH001")!;
        Assert.Equal(51, state.LastSeq);
        Assert.Equal(50, state.SampleSeq);
        Assert.Equal(3000, state.PresentEnergyTakeWh);
    }

    [Fact]
    public async Task Ingest_SameSeqOrderButOlderSampleTime_DoesNotRegressSampleValues()
    {
        // Ordering is by sample time: a higher seq with an older t does not replace the values.
        await _ingestor.IngestAsync(Upload(Sample(50, "2026-10-09T20:30:00Z", takeWh: 3000)), Now, CancellationToken.None);
        await _ingestor.IngestAsync(Upload(Sample(60, "2026-10-09T20:29:00Z", takeWh: 4000)), Now, CancellationToken.None);

        var state = _store.CurrentState("WH001")!;
        Assert.Equal(3000, state.PresentEnergyTakeWh);
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T20:30:00Z"), state.SampleTime);
        Assert.Equal(60, state.LastSeq); // still recorded for information
    }

    [Fact]
    public async Task Ingest_SeqReset_NewerSampleTime_UpdatesCurrentState()
    {
        // NVS erased on reflash: the sequence starts again from 0 under a new bootId.
        await _ingestor.IngestAsync(Upload(Sample(5000, "2026-10-09T20:00:00Z", takeWh: 3000)), Now, CancellationToken.None);
        await _ingestor.IngestAsync(
            Upload(Sample(1, "2026-10-09T20:30:00Z", takeWh: 2000)).WithBoot("0badf00d"), Now, CancellationToken.None);

        var state = _store.CurrentState("WH001")!;
        Assert.Equal(1, state.LastSeq);
        Assert.Equal(1, state.SampleSeq);
        Assert.Equal(2000, state.PresentEnergyTakeWh);
        Assert.Equal("0badf00d", state.BootId);
    }

    [Fact]
    public async Task Ingest_OldBootRetryWithLowerSeqAndOlderTime_DoesNotRegress()
    {
        await _ingestor.IngestAsync(Upload(Sample(5000, "2026-10-09T20:30:00Z", takeWh: 3000)), Now, CancellationToken.None);
        await _ingestor.IngestAsync(
            Upload(Sample(4000, "2026-10-09T20:00:00Z", takeWh: 2000)).WithBoot("0badf00d"), Now, CancellationToken.None);

        var state = _store.CurrentState("WH001")!;
        Assert.Equal(5000, state.LastSeq);
        Assert.Equal(3000, state.PresentEnergyTakeWh);
        Assert.Equal("c4e633a2", state.BootId);
    }

    [Fact]
    public async Task CurrentStateConflict_RereadsAndRetriesOnce()
    {
        await _ingestor.IngestAsync(Upload(Sample(50, "2026-10-09T20:30:00Z")), Now, CancellationToken.None);
        var readsBefore = _store.CurrentStateReads;
        _store.ConflictsToSimulate = 1;

        await _ingestor.IngestAsync(Upload(Sample(51, "2026-10-09T20:30:15Z", takeWh: 1234)), Now, CancellationToken.None);

        Assert.Equal(readsBefore + 2, _store.CurrentStateReads);
        Assert.Equal(1234, _store.CurrentState("WH001")!.PresentEnergyTakeWh);
        Assert.Equal(51, _store.CurrentState("WH001")!.LastSeq);
    }

    [Fact]
    public async Task CurrentStateConflict_MergesWithConcurrentNewerWrite()
    {
        await _ingestor.IngestAsync(Upload(Sample(50, "2026-10-09T20:30:00Z")), Now, CancellationToken.None);
        _store.ConflictsToSimulate = 1;
        _store.OnConflict = s =>
        {
            // A concurrent request stored seq 60 between our read and our write.
            var newer = s.CurrentState("WH001")!;
            newer.LastSeq = 60;
            newer.SampleSeq = 60;
            newer.SampleTime = DateTimeOffset.Parse("2026-10-09T20:30:30Z");
            newer.PresentEnergyTakeWh = 999;
            s.Put(TableNames.CurrentState, newer);
        };

        await _ingestor.IngestAsync(Upload(Sample(51, "2026-10-09T20:30:15Z", takeWh: 1234)), Now, CancellationToken.None);

        var state = _store.CurrentState("WH001")!;
        Assert.Equal(60, state.LastSeq);
        Assert.Equal(999, state.PresentEnergyTakeWh);
    }

    [Fact]
    public async Task CurrentStateConflict_Persistent_StillAcksStoredRecords()
    {
        _store.ConflictsToSimulate = int.MaxValue;
        var resp = await _ingestor.IngestAsync(Example(), Now, CancellationToken.None);

        Assert.Equal(12786u, resp.AckSeq);
        Assert.Single(_store.Rows<TelemetryEntity>(TableNames.Telemetry));
    }

    [Fact]
    public async Task FirstUpload_InsertsCurrentState()
    {
        Assert.Null(_store.CurrentState("WH001"));
        await _ingestor.IngestAsync(Upload(Event(1, "2026-10-09T20:30:00Z", "boot")), Now, CancellationToken.None);

        var state = _store.CurrentState("WH001")!;
        Assert.Equal(1, state.LastSeq);
        Assert.Null(state.SampleSeq);
        Assert.Null(state.OpState);
        Assert.Equal(Now, state.LastSeen);
    }
}
