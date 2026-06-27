using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class FridaServiceTests
{
    [Theory]
    [InlineData("x86_64", "x86_64")]
    [InlineData("x86", "x86")]
    [InlineData("arm64-v8a", "arm64")]
    [InlineData("armeabi-v7a", "arm")]
    [InlineData("", null)]
    public void MapAbiToFridaArch_MapsAndroidAbiNames(string abi, string? expected)
    {
        Assert.Equal(expected, FridaService.MapAbiToFridaArch(abi));
    }

    [Fact]
    public void TrySelectReleaseAsset_PicksMatchingAndroidArchitecture()
    {
        const string json = """
        {
          "tag_name": "16.2.1",
          "assets": [
            {
              "name": "frida-server-16.2.1-android-x86.xz",
              "browser_download_url": "https://example.invalid/frida-x86.xz"
            },
            {
              "name": "frida-server-16.2.1-android-x86_64.xz",
              "browser_download_url": "https://example.invalid/frida-x86_64.xz"
            }
          ]
        }
        """;

        var selected = FridaService.TrySelectReleaseAsset(json, "x86_64");

        Assert.NotNull(selected);
        Assert.Equal("16.2.1", selected.Value.tag);
        Assert.Equal("https://example.invalid/frida-x86_64.xz", selected.Value.url);
    }
}
