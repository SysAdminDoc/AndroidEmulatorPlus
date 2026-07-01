using System.Reflection;
using AndroidEmulatorPlus.ViewModels;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class ConfigViewModelTests
{
    /// <summary>
    /// ConfigViewModel.ParseSizeGb is private static. We invoke via reflection to keep
    /// the SUT internal-API-stable while still covering the raw-byte branch that the
    /// post-v0.1.0 audit fixed.
    /// </summary>
    private static int? ParseSizeGb(string? input)
    {
        var mi = typeof(ConfigViewModel).GetMethod("ParseSizeGb",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (int?)mi.Invoke(null, new object?[] { input });
    }

    [Theory]
    [InlineData("8G", 8)]
    [InlineData("16G", 16)]
    [InlineData("2048M", 2)]
    [InlineData("1048576K", 1)]
    [InlineData("8589934592", 8)]   // 8 GiB in raw bytes — the v0.1.0 regression
    [InlineData(null, null)]
    [InlineData("nonsense", null)]
    public void ParseSizeGb_handles_known_shapes(string? input, int? expected)
    {
        Assert.Equal(expected, ParseSizeGb(input));
    }

    [Theory]
    [InlineData("512M", 1)]
    [InlineData("256M", 1)]
    [InlineData("536870912", 1)]    // 512 MB raw bytes — must clamp to 1
    [InlineData("1024K", 1)]
    public void ParseSizeGb_clamps_sub_gb_to_1(string input, int expected)
    {
        Assert.Equal(expected, ParseSizeGb(input));
    }
}
