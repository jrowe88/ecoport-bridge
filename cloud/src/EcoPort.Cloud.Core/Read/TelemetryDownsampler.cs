using EcoPort.Cloud.Contracts;

namespace EcoPort.Cloud.Core.Read;

/// <summary>Reduces a telemetry series for charting.</summary>
public static class TelemetryDownsampler
{
    /// <summary>
    /// Splits the time span of <paramref name="points"/> into <paramref name="maxPoints"/> equal
    /// buckets. Each non-empty bucket becomes one point: mean time, mean watts (rounded), mean
    /// stored-energy % over the points that have one (null if none), last opstate. Returns the
    /// input unchanged when it already has at most <paramref name="maxPoints"/> points.
    /// </summary>
    public static IReadOnlyList<TelemetryPointDto> Downsample(IReadOnlyList<TelemetryPointDto> points, int maxPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoints);
        if (points.Count <= maxPoints)
        {
            return points;
        }

        var sorted = points.OrderBy(p => p.T).ToList();
        var first = sorted[0].T;
        var spanTicks = (sorted[^1].T - first).Ticks;

        var buckets = new List<TelemetryPointDto>[maxPoints];
        foreach (var p in sorted)
        {
            var index = spanTicks == 0
                ? 0
                : (int)Math.Min(maxPoints - 1, (p.T - first).Ticks * (decimal)maxPoints / spanTicks);
            (buckets[index] ??= []).Add(p);
        }

        var result = new List<TelemetryPointDto>(maxPoints);
        foreach (var bucket in buckets)
        {
            if (bucket is null)
            {
                continue;
            }

            var meanTicks = (long)bucket.Average(p => (decimal)(p.T - first).Ticks);
            var percents = bucket.Where(p => p.StoredEnergyPercent.HasValue).Select(p => p.StoredEnergyPercent!.Value).ToList();
            result.Add(new TelemetryPointDto(
                first + TimeSpan.FromTicks(meanTicks),
                bucket[^1].OpState,
                (int)Math.Round(bucket.Average(p => (double)p.ElectricityRateW), MidpointRounding.AwayFromZero),
                percents.Count == 0 ? null : percents.Average()));
        }

        return result;
    }
}
