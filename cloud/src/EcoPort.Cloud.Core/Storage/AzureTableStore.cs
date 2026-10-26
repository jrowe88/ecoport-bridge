using System.Runtime.CompilerServices;
using Azure;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace EcoPort.Cloud.Core.Storage;

/// <summary>
/// <see cref="ITableStore"/> on Azure Table Storage (or Azurite). The four tables are created on
/// first use if missing.
/// </summary>
public sealed class AzureTableStore(TableServiceClient service) : ITableStore
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    /// <summary>
    /// <c>Storage:ConnectionString</c> when set (Azurite, SWA managed functions), otherwise
    /// <c>Storage:TableEndpoint</c> with <see cref="DefaultAzureCredential"/> (managed identity).
    /// </summary>
    public static TableServiceClient CreateServiceClient(IConfiguration configuration)
    {
        var connectionString = configuration["Storage:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new TableServiceClient(connectionString);
        }

        var endpoint = configuration["Storage:TableEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "Set Storage:ConnectionString or Storage:TableEndpoint (app settings Storage__ConnectionString / Storage__TableEndpoint).");
        }

        return new TableServiceClient(new Uri(endpoint), new DefaultAzureCredential());
    }

    public async Task UpsertBatchAsync(string table, IReadOnlyList<ITableEntity> entities, CancellationToken ct)
    {
        if (entities.Count == 0)
        {
            return;
        }

        if (entities.Count > 100)
        {
            throw new ArgumentException("A table transaction holds at most 100 entities.", nameof(entities));
        }

        var pk = entities[0].PartitionKey;
        if (entities.Any(e => e.PartitionKey != pk))
        {
            throw new ArgumentException("A table transaction must be a single partition.", nameof(entities));
        }

        var client = await TableAsync(table, ct).ConfigureAwait(false);
        var actions = entities.Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e));
        await client.SubmitTransactionAsync(actions, ct).ConfigureAwait(false);
    }

    public async Task<CurrentStateEntity?> GetCurrentStateAsync(string deviceId, CancellationToken ct)
    {
        var client = await TableAsync(TableNames.CurrentState, ct).ConfigureAwait(false);
        var result = await client.GetEntityIfExistsAsync<CurrentStateEntity>(TableKeys.DevicePartition, deviceId, cancellationToken: ct)
            .ConfigureAwait(false);
        return result.HasValue ? result.Value : null;
    }

    public async Task<bool> TryUpsertCurrentStateAsync(CurrentStateEntity entity, ETag ifMatch, CancellationToken ct)
    {
        var client = await TableAsync(TableNames.CurrentState, ct).ConfigureAwait(false);
        try
        {
            if (ifMatch == default)
            {
                await client.AddEntityAsync(entity, ct).ConfigureAwait(false);
            }
            else
            {
                await client.UpdateEntityAsync(entity, ifMatch, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            }

            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return false;
        }
    }

    public async Task<DeviceEntity?> GetDeviceAsync(string deviceId, CancellationToken ct)
    {
        var client = await TableAsync(TableNames.Devices, ct).ConfigureAwait(false);
        var result = await client.GetEntityIfExistsAsync<DeviceEntity>(TableKeys.DevicePartition, deviceId, cancellationToken: ct)
            .ConfigureAwait(false);
        return result.HasValue ? result.Value : null;
    }

    public async IAsyncEnumerable<DeviceEntity> ListDevicesAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var client = await TableAsync(TableNames.Devices, ct).ConfigureAwait(false);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {TableKeys.DevicePartition}");
        await foreach (var e in client.QueryAsync<DeviceEntity>(filter, cancellationToken: ct).ConfigureAwait(false))
        {
            yield return e;
        }
    }

    public async IAsyncEnumerable<TelemetryEntity> QueryTelemetryAsync(
        string deviceId, DateTimeOffset from, DateTimeOffset to, [EnumeratorCancellation] CancellationToken ct)
    {
        if (to < from)
        {
            yield break;
        }

        var client = await TableAsync(TableNames.Telemetry, ct).ConfigureAwait(false);
        var fromDay = from.UtcDateTime.Date;
        var toDay = to.UtcDateTime.Date;
        for (var day = fromDay; day <= toDay; day = day.AddDays(1))
        {
            var pk = TableKeys.TelemetryPartition(deviceId, new DateTimeOffset(day, TimeSpan.Zero));
            var lo = day == fromDay ? TableKeys.TimeOfDay(from) + "_" : "";
            var hi = day == toDay ? TableKeys.TimeOfDay(to) + "_9999999999" : "~";
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {pk} and RowKey ge {lo} and RowKey le {hi}");
            await foreach (var e in client.QueryAsync<TelemetryEntity>(filter, cancellationToken: ct).ConfigureAwait(false))
            {
                yield return e;
            }
        }
    }

    private async ValueTask<TableClient> TableAsync(string table, CancellationToken ct)
    {
        if (!_initialized)
        {
            await _initLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!_initialized)
                {
                    foreach (var name in TableNames.All)
                    {
                        await service.CreateTableIfNotExistsAsync(name, ct).ConfigureAwait(false);
                    }

                    _initialized = true;
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        return service.GetTableClient(table);
    }
}
