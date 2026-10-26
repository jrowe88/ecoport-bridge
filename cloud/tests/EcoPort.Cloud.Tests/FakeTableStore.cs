using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure;
using Azure.Data.Tables;
using EcoPort.Cloud.Core.Storage;

namespace EcoPort.Cloud.Tests;

/// <summary>
/// In-memory <see cref="ITableStore"/> with the same contract as <see cref="AzureTableStore"/>:
/// batches must be one partition and at most 100 entities; CurrentState uses ETags.
/// </summary>
internal sealed class FakeTableStore : ITableStore
{
    private readonly Dictionary<(string Table, string Pk, string Rk), ITableEntity> _rows = new();
    private int _etag;

    /// <summary>Every UpsertBatchAsync call: table and the partition it wrote.</summary>
    public List<(string Table, string Pk, int Count)> Batches { get; } = new();

    public int CurrentStateReads { get; private set; }

    public int CurrentStateWrites { get; private set; }

    /// <summary>Number of upcoming TryUpsertCurrentStateAsync calls that report a conflict.</summary>
    public int ConflictsToSimulate { get; set; }

    /// <summary>Called when a simulated conflict happens, e.g. to let a "concurrent" writer change the row.</summary>
    public Action<FakeTableStore>? OnConflict { get; set; }

    public void AddDevice(string deviceId) =>
        Put(TableNames.Devices, new DeviceEntity { PartitionKey = TableKeys.DevicePartition, RowKey = deviceId });

    public void Put(string table, ITableEntity entity)
    {
        var copy = Clone(entity);
        copy.ETag = NextETag();
        _rows[(table, copy.PartitionKey, copy.RowKey)] = copy;
    }

    public IReadOnlyList<T> Rows<T>(string table) where T : class, ITableEntity =>
        _rows.Where(kv => kv.Key.Table == table)
            .OrderBy(kv => kv.Key.Pk, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Rk, StringComparer.Ordinal)
            .Select(kv => (T)Clone(kv.Value))
            .ToList();

    public CurrentStateEntity? CurrentState(string deviceId) =>
        _rows.TryGetValue((TableNames.CurrentState, TableKeys.DevicePartition, deviceId), out var e)
            ? (CurrentStateEntity)Clone(e)
            : null;

    /// <summary>All rows of a table as text, ignoring Timestamp and ETag; for "identical rows" checks.</summary>
    public string Snapshot(string table) =>
        string.Join("\n", _rows.Where(kv => kv.Key.Table == table)
            .OrderBy(kv => kv.Key.Pk, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Rk, StringComparer.Ordinal)
            .Select(kv => Describe(kv.Value)));

    public Task UpsertBatchAsync(string table, IReadOnlyList<ITableEntity> entities, CancellationToken ct)
    {
        if (entities.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (entities.Count > 100)
        {
            throw new InvalidOperationException("A table transaction holds at most 100 entities.");
        }

        if (entities.Select(e => e.PartitionKey).Distinct().Count() != 1)
        {
            throw new InvalidOperationException("A table transaction must be a single partition.");
        }

        Batches.Add((table, entities[0].PartitionKey, entities.Count));
        foreach (var e in entities)
        {
            Put(table, e);
        }

        return Task.CompletedTask;
    }

    public Task<CurrentStateEntity?> GetCurrentStateAsync(string deviceId, CancellationToken ct)
    {
        CurrentStateReads++;
        return Task.FromResult(CurrentState(deviceId));
    }

    public Task<bool> TryUpsertCurrentStateAsync(CurrentStateEntity entity, ETag ifMatch, CancellationToken ct)
    {
        if (ConflictsToSimulate > 0)
        {
            ConflictsToSimulate--;
            OnConflict?.Invoke(this);
            return Task.FromResult(false);
        }

        var key = (TableNames.CurrentState, entity.PartitionKey, entity.RowKey);
        var exists = _rows.TryGetValue(key, out var current);
        if (ifMatch == default ? exists : !exists || current!.ETag != ifMatch)
        {
            return Task.FromResult(false);
        }

        CurrentStateWrites++;
        Put(TableNames.CurrentState, entity);
        return Task.FromResult(true);
    }

    public Task<DeviceEntity?> GetDeviceAsync(string deviceId, CancellationToken ct) =>
        Task.FromResult(_rows.TryGetValue((TableNames.Devices, TableKeys.DevicePartition, deviceId), out var e)
            ? (DeviceEntity)Clone(e)
            : null);

    public async IAsyncEnumerable<DeviceEntity> ListDevicesAsync([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var d in Rows<DeviceEntity>(TableNames.Devices))
        {
            yield return d;
        }

        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<TelemetryEntity> QueryTelemetryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var row in Rows<TelemetryEntity>(TableNames.Telemetry))
        {
            if (row.PartitionKey.StartsWith(deviceId + "_", StringComparison.Ordinal) && row.T >= from && row.T <= to)
            {
                yield return row;
            }
        }

        await Task.CompletedTask;
    }

    private ETag NextETag() => new(string.Create(CultureInfo.InvariantCulture, $"W/\"{++_etag}\""));

    private static ITableEntity Clone(ITableEntity e)
    {
        var copy = (ITableEntity)Activator.CreateInstance(e.GetType())!;
        foreach (var p in e.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            p.SetValue(copy, p.GetValue(e));
        }

        return copy;
    }

    private static string Describe(ITableEntity e) =>
        string.Join(";", e.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not nameof(ITableEntity.Timestamp) and not nameof(ITableEntity.ETag))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name}={p.GetValue(e)}")));
}
