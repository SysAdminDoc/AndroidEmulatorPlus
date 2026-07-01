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

    [Fact]
    public void BuildReceipt_detects_version_changes_and_generates_rollback()
    {
        var before = new SdkPackageInventory(
            [
                new SdkInstalledPackage("emulator", "36.0.0", "Android Emulator", "emulator"),
                new SdkInstalledPackage("platform-tools", "35.0.2", "Platform-Tools", "platform-tools"),
            ],
            []);
        var after = new SdkPackageInventory(
            [
                new SdkInstalledPackage("emulator", "36.1.2", "Android Emulator", "emulator"),
                new SdkInstalledPackage("platform-tools", "36.0.0", "Platform-Tools", "platform-tools"),
            ],
            []);

        var receipt = SdkmanagerService.BuildReceipt(["emulator", "platform-tools"], before, after);

        Assert.Equal(2, receipt.Changed.Count);
        Assert.Equal("36.0.0", receipt.Changed[0].From);
        Assert.Equal("36.1.2", receipt.Changed[0].To);
        Assert.Equal("35.0.2", receipt.Changed[1].From);
        Assert.Equal("36.0.0", receipt.Changed[1].To);
        Assert.Equal(2, receipt.RollbackCommands.Count);
        Assert.Contains("emulator", receipt.RollbackCommands[0]);
    }

    [Fact]
    public void BuildReceipt_no_changes_when_versions_match()
    {
        var inventory = new SdkPackageInventory(
            [new SdkInstalledPackage("emulator", "36.0.0", "Android Emulator", "emulator")],
            []);

        var receipt = SdkmanagerService.BuildReceipt(["emulator"], inventory, inventory);

        Assert.Empty(receipt.Changed);
        Assert.Empty(receipt.RollbackCommands);
    }

    [Theory]
    [InlineData("[============                             ] 30% Downloading...", 30, "Downloading...")]
    [InlineData("[=========================================] 100% Unzipping... system-images/", 100, "Unzipping... system-images/")]
    [InlineData("[                                         ] 0% Computing updates...", 0, "Computing updates...")]
    public void ParseSdkManagerProgress_extracts_percent_and_status(string line, int expectedPct, string expectedStatus)
    {
        var result = SdkmanagerService.ParseSdkManagerProgress(line);
        Assert.NotNull(result);
        Assert.Equal(expectedPct, result.Value.percent);
        Assert.Equal(expectedStatus, result.Value.status);
    }

    [Theory]
    [InlineData("Installed packages:")]
    [InlineData("  emulator                                    | 36.0.0  | Android Emulator")]
    [InlineData("")]
    public void ParseSdkManagerProgress_returns_null_for_non_progress_lines(string line)
    {
        Assert.Null(SdkmanagerService.ParseSdkManagerProgress(line));
    }

    [Fact]
    public void BuildReceipt_handles_newly_installed_package()
    {
        var before = new SdkPackageInventory([], []);
        var after = new SdkPackageInventory(
            [new SdkInstalledPackage("system-images;android-36;google_apis;x86_64", "6", "Google APIs", "system-images")],
            []);

        var receipt = SdkmanagerService.BuildReceipt(["system-images;android-36;google_apis;x86_64"], before, after);

        Assert.Single(receipt.Changed);
        Assert.Equal("not installed", receipt.Changed[0].From);
        Assert.Equal("6", receipt.Changed[0].To);
        Assert.Empty(receipt.RollbackCommands);
    }
}
