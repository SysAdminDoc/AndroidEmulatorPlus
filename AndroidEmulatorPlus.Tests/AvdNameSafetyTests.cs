using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class AvdNameSafetyTests
{
    [Theory]
    [InlineData("Pixel_8_API_35")]
    [InlineData("test.avd-copy_1")]
    public void IsSafeAvdName_accepts_supported_names(string value)
    {
        Assert.True(AvdService.IsSafeAvdName(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Pixel 8")]
    [InlineData("../Pixel")]
    [InlineData("Pixel\\8")]
    [InlineData("Pixel;del")]
    public void IsSafeAvdName_rejects_path_and_shell_syntax(string value)
    {
        Assert.False(AvdService.IsSafeAvdName(value));
    }

    [Theory]
    [InlineData("default_boot")]
    [InlineData("Before install")]
    [InlineData("release-2026.05")]
    public void IsSafeSnapshotName_accepts_supported_names(string value)
    {
        Assert.True(SnapshotService.IsSafeSnapshotName(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../snap")]
    [InlineData("snap\\name")]
    [InlineData("snap;rm")]
    public void IsSafeSnapshotName_rejects_path_and_shell_syntax(string value)
    {
        Assert.False(SnapshotService.IsSafeSnapshotName(value));
    }
}
