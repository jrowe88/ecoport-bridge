using System.Globalization;

namespace EcoPort.Cloud.Core.Storage;

/// <summary>
/// Table keys (docs/architecture.md §7). Keys are built only from record contents, so a retried
/// upload writes the same rows. All times are converted to UTC first.
/// </summary>
public static class TableKeys
{
    /// <summary>PartitionKey of <c>Devices</c> and <c>CurrentState</c>; the RowKey is the device ID.</summary>
    public const string DevicePartition = "DEVICE";

    /// <summary><c>Telemetry</c>: PK <c>{deviceId}_{yyyyMMdd}</c>, RK <c>{HHmmssfff}_{seq:D10}</c>.</summary>
    public static (string Pk, string Rk) Telemetry(string deviceId, DateTimeOffset t, uint seq) =>
        (TelemetryPartition(deviceId, t), $"{TimeOfDay(t)}_{Seq(seq)}");

    /// <summary><c>DeviceEvents</c>: PK <c>{deviceId}</c>, RK <c>{yyyyMMddTHHmmssfff}_{seq:D10}</c>.</summary>
    public static (string Pk, string Rk) DeviceEvent(string deviceId, DateTimeOffset t, uint seq) =>
        (deviceId, $"{t.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture)}_{Seq(seq)}");

    /// <summary>The Telemetry partition holding UTC day of <paramref name="t"/>.</summary>
    public static string TelemetryPartition(string deviceId, DateTimeOffset t) =>
        $"{deviceId}_{t.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

    /// <summary>The <c>HHmmssfff</c> prefix of a Telemetry RowKey.</summary>
    public static string TimeOfDay(DateTimeOffset t) =>
        t.UtcDateTime.ToString("HHmmssfff", CultureInfo.InvariantCulture);

    private static string Seq(uint seq) => seq.ToString("D10", CultureInfo.InvariantCulture);
}
