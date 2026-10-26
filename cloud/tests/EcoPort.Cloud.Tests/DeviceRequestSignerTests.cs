using System.Text;
using EcoPort.Cloud.Core.Auth;

namespace EcoPort.Cloud.Tests;

public class DeviceRequestSignerTests
{
    private static readonly byte[] Secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private const string Path = "/device/v1/records";
    private const string Timestamp = "2026-10-09T20:31:02Z";
    private static readonly DateTimeOffset TimestampValue = new(2026, 10, 9, 20, 31, 2, TimeSpan.Zero);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"hello":"world"}""");

    private static string Sign(byte[] secret) =>
        DeviceRequestSigner.ComputeSignature(secret, "POST", Path, Timestamp, Body);

    [Fact]
    public void SigningVectors_Match()
    {
        using var doc = ContractFiles.Json("signing-vectors.json");
        var secret = Convert.FromBase64String(doc.RootElement.GetProperty("secretBase64").GetString()!);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToList();
        Assert.Equal(3, vectors.Count);
        foreach (var v in vectors)
        {
            var body = Convert.FromBase64String(v.GetProperty("bodyBase64").GetString()!);
            var actual = DeviceRequestSigner.ComputeSignature(
                secret,
                v.GetProperty("method").GetString()!,
                v.GetProperty("path").GetString()!,
                v.GetProperty("timestamp").GetString()!,
                body);
            Assert.Equal(v.GetProperty("signature").GetString(), actual);
        }
    }

    [Fact]
    public void SigningVector_BodyIsTheExampleFile()
    {
        // Guards against the example being edited without regenerating the vectors.
        using var doc = ContractFiles.Json("signing-vectors.json");
        var first = doc.RootElement.GetProperty("vectors")[0];
        Assert.Equal(
            ContractFiles.Bytes("upload-request.example.json"),
            Convert.FromBase64String(first.GetProperty("bodyBase64").GetString()!));
    }

    [Fact]
    public void Verify_ValidSignature_Accepted()
    {
        var now = TimestampValue + TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59);
        Assert.True(DeviceRequestSigner.Verify(Secret, "POST", Path, Timestamp, Sign(Secret), Body, now));
        var before = TimestampValue - TimeSpan.FromMinutes(4) - TimeSpan.FromSeconds(59);
        Assert.True(DeviceRequestSigner.Verify(Secret, "POST", Path, Timestamp, Sign(Secret), Body, before));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(-301)]
    public void Verify_TimestampSkewOver5Min_Rejected(int skewSeconds)
    {
        var now = TimestampValue + TimeSpan.FromSeconds(skewSeconds);
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, Timestamp, Sign(Secret), Body, now));
    }

    [Fact]
    public void Verify_WrongSecret_Rejected()
    {
        var other = Enumerable.Repeat((byte)7, 32).ToArray();
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, Timestamp, Sign(other), Body, TimestampValue));
    }

    [Fact]
    public void Verify_NullSecret_Rejected()
    {
        Assert.False(DeviceRequestSigner.Verify(null, "POST", Path, Timestamp, Sign(Secret), Body, TimestampValue));
    }

    [Fact]
    public void Verify_MissingHeaders_Rejected()
    {
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, null, Sign(Secret), Body, TimestampValue));
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, Timestamp, null, Body, TimestampValue));
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, "", Sign(Secret), Body, TimestampValue));
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, Timestamp, "", Body, TimestampValue));
    }

    [Theory]
    [InlineData("not a time")]
    [InlineData("2026-10-09T20:31:02.000Z")] // milliseconds are not the header format
    [InlineData("2026-10-09T20:31:02+00:00")]
    [InlineData("2026-10-09 20:31:02Z")]
    public void Verify_BadTimestampFormat_Rejected(string timestamp)
    {
        var sig = DeviceRequestSigner.ComputeSignature(Secret, "POST", Path, timestamp, Body);
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, timestamp, sig, Body, TimestampValue));
    }

    [Fact]
    public void Verify_TamperedBodyOrPath_Rejected()
    {
        var sig = Sign(Secret);
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, Timestamp, sig, Encoding.UTF8.GetBytes("{}"), TimestampValue));
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", "/device/v1/other", Timestamp, sig, Body, TimestampValue));
        Assert.False(DeviceRequestSigner.Verify(Secret, "PUT", Path, Timestamp, sig, Body, TimestampValue));
    }

    [Fact]
    public void Verify_GarbageSignature_Rejected()
    {
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, Timestamp, "%%%not-base64%%%", Body, TimestampValue));
        Assert.False(DeviceRequestSigner.Verify(Secret, "POST", Path, Timestamp, "AAAA", Body, TimestampValue));
    }
}
