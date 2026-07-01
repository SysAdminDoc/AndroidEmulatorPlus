using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class EmulatorServiceTests
{
    [Fact]
    public void LaunchOptions_defaults_are_all_off()
    {
        var opt = new EmulatorService.LaunchOptions();
        Assert.False(opt.ColdBoot);
        Assert.False(opt.WipeData);
        Assert.False(opt.NoWindow);
        Assert.False(opt.NoAudio);
        Assert.False(opt.MultiDisplay);
        Assert.False(opt.PeerNetworking);
        Assert.Null(opt.HttpProxy);
        Assert.Null(opt.DnsServer);
        Assert.Null(opt.FrontCamera);
        Assert.Null(opt.BackCamera);
        Assert.Null(opt.GpuMode);
    }

    [Fact]
    public void LaunchOptions_with_record_produces_correct_copy()
    {
        var original = new EmulatorService.LaunchOptions(ColdBoot: true, PeerNetworking: true);
        var copy = original with { NoAudio = true };

        Assert.True(copy.ColdBoot);
        Assert.True(copy.PeerNetworking);
        Assert.True(copy.NoAudio);
        Assert.False(original.NoAudio);
    }

    [Fact]
    public void AccelStatus_reports_ok_flag()
    {
        var good = new EmulatorService.AccelStatus(true, "Hyper-V enabled", "detailed output");
        var bad = new EmulatorService.AccelStatus(false, "HAXM not installed", "");

        Assert.True(good.Ok);
        Assert.False(bad.Ok);
        Assert.Contains("Hyper-V", good.Summary);
    }
}
