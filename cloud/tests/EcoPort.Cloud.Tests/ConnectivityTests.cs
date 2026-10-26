using EcoPort.Cloud.Core.Read;

namespace EcoPort.Cloud.Tests;

public class ConnectivityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-09T20:31:02Z");

    [Theory]
    [InlineData(0, "online")]
    [InlineData(180, "online")]
    [InlineData(181, "delayed")]
    [InlineData(900, "delayed")]
    [InlineData(901, "offline")]
    [InlineData(-30, "online")] // device clock slightly ahead is still online
    public void From_SecondsSinceLastSeen(int seconds, string expected)
    {
        Assert.Equal(expected, Connectivity.From(Now - TimeSpan.FromSeconds(seconds), Now));
    }

    [Fact]
    public void From_Null_IsOffline() => Assert.Equal(Connectivity.Offline, Connectivity.From(null, Now));
}
