using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using EcoPort.Cloud.Contracts;
using EcoPort.Cloud.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EcoPort.Cloud.Core.Ingest;

/// <summary>
/// Stores a validated upload: samples to <c>Telemetry</c> (one transaction per UTC-day partition),
/// events and health to <c>DeviceEvents</c>, then moves <c>CurrentState</c> forward (never back)
/// with optimistic concurrency. Raw rows are idempotent upserts, so a retried upload is harmless.
/// </summary>
public sealed class RecordIngestor(ITableStore store, ILogger<RecordIngestor>? logger = null)
{
    /// <summary>Attempts at the CurrentState read-merge-write before giving up for this request.</summary>
    public const int MaxCurrentStateAttempts = 5;

    private const int MaxBatch = 100;

    private readonly ILogger _logger = logger ?? NullLogger<RecordIngestor>.Instance;

    /// <param name="req">A request that passed <see cref="UploadValidator"/>.</param>
    public async Task<UploadResponse> IngestAsync(UploadRequest req, DateTimeOffset now, CancellationToken ct)
    {
        var samples = req.Records.Where(r => r.Kind == RecordKinds.Sample).ToList();
        var others = req.Records.Where(r => r.Kind != RecordKinds.Sample).ToList();

        var telemetry = samples
            .Select(r => ToTelemetry(req, r))
            .GroupBy(e => e.PartitionKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var partition in telemetry)
        {
            foreach (var chunk in partition.Chunk(MaxBatch))
            {
                await store.UpsertBatchAsync(TableNames.Telemetry, chunk, ct).ConfigureAwait(false);
            }
        }

        foreach (var chunk in others.Select(r => ToEvent(req, r)).Chunk(MaxBatch))
        {
            await store.UpsertBatchAsync(TableNames.DeviceEvents, chunk, ct).ConfigureAwait(false);
        }

        await UpdateCurrentStateAsync(req, now, ct).ConfigureAwait(false);

        return new UploadResponse(req.Records.Max(r => r.Seq), now, CommandsPending: false);
    }

    private async Task UpdateCurrentStateAsync(UploadRequest req, DateTimeOffset now, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxCurrentStateAttempts; attempt++)
        {
            var existing = await store.GetCurrentStateAsync(req.DeviceId, ct).ConfigureAwait(false);
            var next = Merge(existing, req, now);
            if (next is null)
            {
                return;
            }

            if (await store.TryUpsertCurrentStateAsync(next, existing?.ETag ?? default, ct).ConfigureAwait(false))
            {
                return;
            }
        }

        // The raw rows are stored, so the records are still acknowledged; the next upload catches
        // CurrentState up.
        _logger.LogWarning(
            "CurrentState for {DeviceId} not updated after {Attempts} conflicting attempts.",
            req.DeviceId,
            MaxCurrentStateAttempts);
    }

    /// <summary>
    /// The CurrentState after applying <paramref name="req"/>, or null when nothing changes.
    /// Sample values move forward only when the request's latest sample has a later <c>t</c> than
    /// the stored <c>SampleTime</c>; <c>LastSeen</c> only moves forward. <c>LastSeq</c>, boot ID and
    /// firmware follow the newest sample, or newer records from the same boot.
    /// </summary>
    internal static CurrentStateEntity? Merge(CurrentStateEntity? existing, UploadRequest req, DateTimeOffset now)
    {
        long maxSeq = req.Records.Max(r => r.Seq);
        var sample = req.Records.Where(r => r.Kind == RecordKinds.Sample).MaxBy(r => r.Seq);

        if (existing is null)
        {
            var created = new CurrentStateEntity
            {
                PartitionKey = TableKeys.DevicePartition,
                RowKey = req.DeviceId,
                LastSeen = now,
            };
            ApplyMeta(created, req, maxSeq);
            if (sample is not null)
            {
                ApplySample(created, sample);
            }

            return created;
        }

        var next = Copy(existing);
        var changed = false;
        var sameBoot = string.Equals(req.BootId, existing.BootId, StringComparison.Ordinal);

        // Ordered by sample time, not seq: an NVS erase restarts seq at 0, and the dashboard must
        // not freeze when that happens.
        var sampleNewer = sample is not null
            && (existing.SampleTime is not { } storedTime || sample.T!.Value > storedTime);

        if (sampleNewer)
        {
            ApplySample(next, sample!);
            ApplyMeta(next, req, sameBoot ? Math.Max(existing.LastSeq, maxSeq) : maxSeq);
            changed = true;
        }
        else if (sameBoot && maxSeq > existing.LastSeq)
        {
            // e.g. an event-only upload: newer records from the boot already shown.
            ApplyMeta(next, req, maxSeq);
            changed = true;
        }

        if (now > existing.LastSeen)
        {
            next.LastSeen = now;
            changed = true;
        }

        return changed ? next : null;
    }

    private static void ApplyMeta(CurrentStateEntity e, UploadRequest req, long maxSeq)
    {
        e.LastSeq = maxSeq;
        e.BootId = req.BootId;
        e.Firmware = req.Firmware;
        e.Simulated = req.Simulated;
    }

    private static void ApplySample(CurrentStateEntity e, DeviceRecord sample)
    {
        e.SampleSeq = sample.Seq;
        e.SampleTime = sample.T!.Value.ToUniversalTime();
        e.OpState = sample.OpState;
        e.ElectricityRateW = sample.Commodity!.ElectricityRateW;
        e.TotalStorageCapacityWh = sample.Commodity.TotalStorageCapacityWh;
        e.PresentEnergyTakeWh = sample.Commodity.PresentEnergyTakeWh;
    }

    private static CurrentStateEntity Copy(CurrentStateEntity e) => new()
    {
        PartitionKey = e.PartitionKey,
        RowKey = e.RowKey,
        LastSeen = e.LastSeen,
        LastSeq = e.LastSeq,
        BootId = e.BootId,
        Firmware = e.Firmware,
        Simulated = e.Simulated,
        SampleSeq = e.SampleSeq,
        SampleTime = e.SampleTime,
        OpState = e.OpState,
        ElectricityRateW = e.ElectricityRateW,
        TotalStorageCapacityWh = e.TotalStorageCapacityWh,
        PresentEnergyTakeWh = e.PresentEnergyTakeWh,
    };

    private static TelemetryEntity ToTelemetry(UploadRequest req, DeviceRecord r)
    {
        var t = r.T!.Value.ToUniversalTime();
        var (pk, rk) = TableKeys.Telemetry(req.DeviceId, t, r.Seq);
        return new TelemetryEntity
        {
            PartitionKey = pk,
            RowKey = rk,
            Seq = r.Seq,
            T = t,
            UptimeMs = ClampToLong(r.UptimeMs),
            BootId = req.BootId,
            Simulated = req.Simulated,
            OpState = r.OpState!.Value,
            ElectricityRateW = r.Commodity!.ElectricityRateW,
            ElectricityCumulativeWh = r.Commodity.ElectricityCumulativeWh,
            TotalStorageCapacityWh = r.Commodity.TotalStorageCapacityWh,
            PresentEnergyTakeWh = r.Commodity.PresentEnergyTakeWh,
            LinkOk = r.Link?.Ok,
            LinkRetries = r.Link?.Retries,
            LinkNaks = r.Link?.Naks,
        };
    }

    private static DeviceEventEntity ToEvent(UploadRequest req, DeviceRecord r)
    {
        var t = r.T!.Value.ToUniversalTime();
        var (pk, rk) = TableKeys.DeviceEvent(req.DeviceId, t, r.Seq);
        return new DeviceEventEntity
        {
            PartitionKey = pk,
            RowKey = rk,
            Seq = r.Seq,
            Kind = r.Kind,
            T = t,
            UptimeMs = ClampToLong(r.UptimeMs),
            BootId = req.BootId,
            Simulated = req.Simulated,
            Type = r.Type,
            DataJson = r.Data is { ValueKind: not JsonValueKind.Undefined } data ? JsonSerializer.Serialize(data) : null,
            RssiDbm = r.RssiDbm,
            FreeHeapBytes = r.FreeHeapBytes,
            BufferDepth = r.BufferDepth,
            ResetReason = r.ResetReason,
        };
    }

    private static long ClampToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;
}
