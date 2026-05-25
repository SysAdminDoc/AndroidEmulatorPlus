using System.Reflection;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class MigrationServiceTests
{
    private static string ParseFailReason(string input)
    {
        var mi = typeof(MigrationService).GetMethod("ParseFailReason",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)mi.Invoke(null, new object?[] { input })!;
    }

    [Fact]
    public void Returns_INSTALL_FAILED_token_when_present()
    {
        var line = "[INSTALL_FAILED_INSUFFICIENT_STORAGE: not enough space]";
        Assert.Equal("INSTALL_FAILED_INSUFFICIENT_STORAGE", ParseFailReason(line));
    }

    [Fact]
    public void Returns_INSTALL_FAILED_token_inline_with_other_text()
    {
        var line = "Performing Streamed Install\nINSTALL_FAILED_VERSION_DOWNGRADE\n";
        Assert.Equal("INSTALL_FAILED_VERSION_DOWNGRADE", ParseFailReason(line));
    }

    [Fact]
    public void Fallback_when_no_known_token()
    {
        Assert.Equal("install failed", ParseFailReason("Cleanup deleted temp paths"));
    }
}
