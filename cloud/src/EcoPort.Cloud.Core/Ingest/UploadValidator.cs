using System.Globalization;
using EcoPort.Cloud.Contracts;

namespace EcoPort.Cloud.Core.Ingest;

/// <summary>Validation rules of contracts/device-v1/README.md ("Validation (server)").</summary>
public static class UploadValidator
{
    public const int MaxRecords = 100;
    public static readonly TimeSpan MaxFuture = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <returns>Null when valid, otherwise a message for the <c>400</c> body.</returns>
    public static string? Validate(UploadRequest req, string headerDeviceId, DateTimeOffset now)
    {
        if (!string.Equals(req.DeviceId, headerDeviceId, StringComparison.Ordinal))
        {
            return "deviceId does not match X-Device-Id.";
        }

        if (req.Records is null || req.Records.Count == 0)
        {
            return "records must contain at least 1 record.";
        }

        if (req.Records.Count > MaxRecords)
        {
            return string.Create(CultureInfo.InvariantCulture, $"records must contain at most {MaxRecords} records (got {req.Records.Count}).");
        }

        uint? previous = null;
        for (var i = 0; i < req.Records.Count; i++)
        {
            var r = req.Records[i];
            if (r is null)
            {
                return Invalid(i, "is null");
            }

            if (previous is { } p && r.Seq <= p)
            {
                return Invalid(i, $"seq {r.Seq} is not greater than the previous seq {p}");
            }

            previous = r.Seq;

            switch (r.Kind)
            {
                case RecordKinds.Sample:
                    if (r.OpState is null)
                    {
                        return Invalid(i, "is a sample without opState");
                    }

                    if (r.Commodity is null)
                    {
                        return Invalid(i, "is a sample without commodity");
                    }

                    break;
                case RecordKinds.Event:
                    if (string.IsNullOrEmpty(r.Type))
                    {
                        return Invalid(i, "is an event without type");
                    }

                    break;
                case RecordKinds.Health:
                    break;
                default:
                    return Invalid(i, $"has unknown kind '{r.Kind}' (expected sample, event or health)");
            }

            if (r.T is not { } t)
            {
                return Invalid(i, "has no t");
            }

            if (t - now > MaxFuture)
            {
                return Invalid(i, "t is more than 5 minutes in the future");
            }

            if (now - t > MaxAge)
            {
                return Invalid(i, "t is more than 30 days in the past");
            }
        }

        return null;
    }

    private static string Invalid(int index, string problem) =>
        string.Create(CultureInfo.InvariantCulture, $"records[{index}] {problem}.");
}
