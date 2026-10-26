using System.Net;
using System.Text.Json;
using EcoPort.Web.Services;
using MudBlazor;

namespace EcoPort.Web.Tests;

public sealed class DisplayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 20, 31, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("online", Color.Success)]
    [InlineData("delayed", Color.Warning)]
    [InlineData("offline", Color.Error)]
    [InlineData("something new", Color.Error)]
    public void ConnectivityColor(string connectivity, Color expected) =>
        Assert.Equal(expected, Display.ConnectivityColor(connectivity));

    [Theory]
    [InlineData(2, "just now")]
    [InlineData(40, "40 s ago")]
    [InlineData(185, "3 min ago")]
    [InlineData(7300, "2 h ago")]
    [InlineData(200000, "2 d ago")]
    public void Ago(int secondsAgo, string expected) =>
        Assert.Equal(expected, Display.Ago(Now.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void Ago_Null_IsNever() => Assert.Equal("never", Display.Ago(null, Now));

    [Fact]
    public void LoadError_NonJson_ExplainsLikelyCause() =>
        Assert.Contains("signed out", Display.LoadError(new JsonException("'<' is an invalid start of a value")));

    [Fact]
    public void LoadError_HttpStatus_IsShown() =>
        Assert.Equal("the API returned HTTP 500 InternalServerError",
            Display.LoadError(new HttpRequestException("x", null, HttpStatusCode.InternalServerError)));
}
