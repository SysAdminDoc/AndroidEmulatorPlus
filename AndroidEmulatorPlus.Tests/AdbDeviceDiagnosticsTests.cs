using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class AdbDeviceDiagnosticsTests
{
    [Theory]
    [InlineData("R5CT123456A", false, "usb")]
    [InlineData("192.168.1.25:5555", false, "wireless")]
    [InlineData("emulator-5554", true, "emulator")]
    public void InferTransport_labels_emulator_wireless_and_usb(string serial, bool isEmulator, string expected)
    {
        var device = new Device(serial, "device", "", "", isEmulator);

        Assert.Equal(expected, AdbService.InferTransport(device));
    }

    [Fact]
    public void ParsePlatformToolsVersion_reads_adb_version_output()
    {
        const string stdout = """
        Android Debug Bridge version 1.0.41
        Version 36.0.0-13206524
        Installed as C:\Android\platform-tools\adb.exe
        """;

        Assert.Equal("1.0.41", AdbService.ParsePlatformToolsVersion(stdout));
    }

    [Fact]
    public void ParseDeviceBuildProps_reads_api_and_security_patch()
    {
        var (api, patch) = AdbService.ParseDeviceBuildProps("35\r\n2026-05-05\r\n");

        Assert.Equal("35", api);
        Assert.Equal("2026-05-05", patch);
    }

    [Fact]
    public void IsSecurityPatchStale_flags_patches_older_than_threshold()
    {
        var now = new DateTime(2026, 06, 28, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(AdbService.IsSecurityPatchStale("2025-12-01", now));
        Assert.False(AdbService.IsSecurityPatchStale("2026-05-05", now));
        Assert.False(AdbService.IsSecurityPatchStale(null, now));
    }
}
