using EcoPort.Cloud.Contracts;
using EcoPort.Cloud.Core.Read;

namespace EcoPort.Cloud.Tests;

public class TelemetryDownsamplerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-09T00:00:00Z");

    private static List<TelemetryPointDto> Day(int count = 5760) =>
        Enumerable.Range(0, count)
            .Select(i => new TelemetryPointDto(Start.AddSeconds(15 * i), i % 2, 200 + (i % 100), 50.0 + (i % 10)))
            .ToList();

    [Fact]
    public void FullDay_DownsamplesTo500_WithinRange()
    {
        var points = Day();
        var result = TelemetryDownsampler.Downsample(points, 500);

        Assert.Equal(500, result.Count);
        Assert.InRange(result[0].T, points[0].T, points[^1].T);
        Assert.InRange(result[^1].T, points[0].T, points[^1].T);
        Assert.True(result.Zip(result.Skip(1)).All(p => p.First.T < p.Second.T), "times strictly increasing");
    }

    [Fact]
    public void AlreadySmall_Unchanged()
    {
        var points = Day(300);
        Assert.Same(points, TelemetryDownsampler.Downsample(points, 500));
        Assert.Empty(TelemetryDownsampler.Downsample([], 500));
    }

    [Fact]
    public void Bucket_MeanWattsAndPercent_LastOpState()
    {
        var points = new List<TelemetryPointDto>
        {
            new(Start, 1, 100, 40.0),
            new(Start.AddSeconds(15), 1, 200, null),
            new(Start.AddSeconds(30), 0, 301, 60.0),
            new(Start.AddSeconds(45), 0, 0, 10.0),
        };

        var result = TelemetryDownsampler.Downsample(points, 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(150, result[0].ElectricityRateW);
        Assert.Equal(40.0, result[0].StoredEnergyPercent); // nulls are skipped in the mean
        Assert.Equal(1, result[0].OpState);
        Assert.Equal(151, result[1].ElectricityRateW); // 150.5 rounds away from zero
        Assert.Equal(35.0, result[1].StoredEnergyPercent);
        Assert.Equal(0, result[1].OpState);
    }

    [Fact]
    public void Bucket_AllPercentNull_StaysNull()
    {
        var points = Enumerable.Range(0, 10).Select(i => new TelemetryPointDto(Start.AddSeconds(i), 1, 100, null)).ToList();
        var result = TelemetryDownsampler.Downsample(points, 2);
        Assert.All(result, p => Assert.Null(p.StoredEnergyPercent));
    }

    [Fact]
    public void SameTimestamp_DoesNotDivideByZero()
    {
        var points = Enumerable.Range(0, 10).Select(i => new TelemetryPointDto(Start, 1, 100 + i, 50)).ToList();
        var result = TelemetryDownsampler.Downsample(points, 3);
        var p = Assert.Single(result);
        Assert.Equal(Start, p.T);
    }

    [Fact]
    public void UnsortedInput_IsHandled()
    {
        var points = Day(1000);
        points.Reverse();
        var result = TelemetryDownsampler.Downsample(points, 100);
        Assert.Equal(100, result.Count);
        Assert.True(result[0].T < result[^1].T);
    }

    [Fact]
    public void MaxPointsMustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TelemetryDownsampler.Downsample(Day(10), 0));
}
