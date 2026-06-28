using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class SdkmanagerServiceTests
{
    [Fact]
    public void ParsePackageInventory_reads_installed_packages_and_available_updates()
    {
        const string output = """
        Installed packages:
          Path                                        | Version | Description              | Location
          -------                                     | ------- | -------                  | -------
          emulator                                    | 36.0.0  | Android Emulator         | emulator
          platform-tools                              | 35.0.2  | Android SDK Platform-Tools| platform-tools
          system-images;android-36;google_apis;x86_64 | 6       | Google APIs Intel x86_64 | system-images\android-36

        Available Updates:
          ID                                          | Installed | Available
          -------                                     | -------   | -------
          emulator                                    | 36.0.0    | 36.1.2
          platform-tools                              | 35.0.2    | 36.0.0

        Available Packages:
          system-images;android-37;google_apis;x86_64 | 1 | Google APIs Intel x86_64
        """;

        var inventory = SdkmanagerService.ParsePackageInventory(output);

        Assert.Equal(3, inventory.Installed.Count);
        Assert.Equal(2, inventory.Updates.Count);
        Assert.Equal("emulator", inventory.Updates[0].Path);
        Assert.Equal("36.0.0", inventory.Updates[0].InstalledVersion);
        Assert.Equal("36.1.2", inventory.Updates[0].AvailableVersion);
        Assert.Equal("Android Emulator", inventory.Updates[0].Description);
    }

    [Theory]
    [InlineData("emulator", true)]
    [InlineData("platform-tools", true)]
    [InlineData("system-images;android-36;google_apis;x86_64", true)]
    [InlineData("build-tools;36.0.0", false)]
    [InlineData("extras;google;usb_driver", false)]
    public void IsUpdateManagedByAep_filters_supported_update_targets(string path, bool expected)
    {
        var update = new SdkPackageUpdate(path, "1", "2", "");

        Assert.Equal(expected, SdkmanagerService.IsUpdateManagedByAep(update));
    }
}
