using EcoPort.Cloud.Core.Storage;

namespace EcoPort.Cloud.Tests;

public class TableKeysTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-10-09T20:30:00.120Z");

    [Fact]
    public void Telemetry_Keys()
    {
        Assert.Equal(("WH001_20261009", "203000120_0000012783"), TableKeys.Telemetry("WH001", T, 12783));
    }

    [Fact]
    public void DeviceEvent_Keys()
    {
        Assert.Equal(("WH001", "20261009T203000120_0000012783"), TableKeys.DeviceEvent("WH001", T, 12783));
    }

    [Fact]
    public void NonUtcOffset_IsConvertedToUtcFirst()
    {
        var local = DateTimeOffset.Parse("2026-10-09T22:30:00.120+02:00");
        Assert.Equal(TableKeys.Telemetry("WH001", T, 12783), TableKeys.Telemetry("WH001", local, 12783));
        Assert.Equal(TableKeys.DeviceEvent("WH001", T, 12783), TableKeys.DeviceEvent("WH001", local, 12783));

        // 23:30 at -05:00 is the next UTC day.
        var west = DateTimeOffset.Parse("2026-10-09T23:30:00.000-05:00");
        Assert.Equal(("WH001_20261010", "043000000_0000000001"), TableKeys.Telemetry("WH001", west, 1));
    }

    [Fact]
    public void MaxSeq_IsTenDigits()
    {
        Assert.Equal("203000120_4294967295", TableKeys.Telemetry("WH001", T, uint.MaxValue).Rk);
    }

    [Fact]
    public void CurrentStateAndDevices_UseDevicePartition()
    {
        Assert.Equal("DEVICE", TableKeys.DevicePartition);
    }
}
