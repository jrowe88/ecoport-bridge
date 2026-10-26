using Azure;
using Azure.Data.Tables;

namespace EcoPort.Cloud.Core.Storage;

/// <summary>Table access used by the device API and the user API.</summary>
public interface ITableStore
{
    /// <summary>Upsert (replace) up to 100 entities of one partition in a single transaction.</summary>
    Task UpsertBatchAsync(string table, IReadOnlyList<ITableEntity> entities, CancellationToken ct);

    /// <summary>The device's <c>CurrentState</c> row with its ETag, or null.</summary>
    Task<CurrentStateEntity?> GetCurrentStateAsync(string deviceId, CancellationToken ct);

    /// <summary>
    /// Writes the row if it is unchanged since read: <paramref name="ifMatch"/> is the ETag from
    /// <see cref="GetCurrentStateAsync"/>, or <c>default</c> to insert a row that must not exist yet.
    /// </summary>
    /// <returns>False when another writer got there first (412 / 409).</returns>
    Task<bool> TryUpsertCurrentStateAsync(CurrentStateEntity entity, ETag ifMatch, CancellationToken ct);

    Task<DeviceEntity?> GetDeviceAsync(string deviceId, CancellationToken ct);

    IAsyncEnumerable<DeviceEntity> ListDevicesAsync(CancellationToken ct);

    /// <summary>Samples with <paramref name="from"/> ≤ t ≤ <paramref name="to"/>, oldest first.</summary>
    IAsyncEnumerable<TelemetryEntity> QueryTelemetryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
