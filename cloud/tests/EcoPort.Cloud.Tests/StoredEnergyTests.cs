using EcoPort.Cloud.Core.Read;

namespace EcoPort.Cloud.Tests;

public class StoredEnergyTests
{
    [Fact]
    public void Percent_FromCapture() => Assert.Equal(65.02, StoredEnergy.Percent(12011, 4202)!.Value, 2);

    [Fact]
    public void Percent_FullTank() => Assert.Equal(100.0, StoredEnergy.Percent(12011, 0));

    [Fact]
    public void Percent_TakeAboveCapacity_ClampsToZero() => Assert.Equal(0.0, StoredEnergy.Percent(12011, 13000));

    [Fact]
    public void Percent_NegativeTake_ClampsTo100() => Assert.Equal(100.0, StoredEnergy.Percent(12011, -5));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 4202)]
    [InlineData(-1, 0)]
    public void Percent_ZeroOrNegativeCapacity_IsNull(int capacity, int take)
    {
        // Vacation mode reports capacity 0: no divide-by-zero, no NaN.
        Assert.Null(StoredEnergy.Percent(capacity, take));
    }

    [Fact]
    public void Percent_NullableOverload_NullWhenEitherMissing()
    {
        Assert.Null(StoredEnergy.Percent(null, 4202));
        Assert.Null(StoredEnergy.Percent(12011, null));
        Assert.Equal(StoredEnergy.Percent(12011, 4202), StoredEnergy.Percent((int?)12011, (int?)4202));
    }
}
