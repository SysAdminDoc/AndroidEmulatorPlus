using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void AppSettings_defaults_are_sensible()
    {
        var s = new AppSettings();
        Assert.Equal("mocha", s.Theme);
        Assert.Null(s.SdkRootOverride);
        Assert.Null(s.MediaDir);
        Assert.Null(s.HttpProxy);
        Assert.False(s.HasSeenWizard);
        Assert.False(s.AutoScrcpy);
        Assert.True(s.AutoUpdateChecks);
    }

    [Fact]
    public void ReadThemeFromDisk_returns_mocha_when_no_file()
    {
        Assert.Equal("mocha", SettingsService.ReadThemeFromDisk());
    }
}
