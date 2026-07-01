using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class ThemeServiceTests
{
    [Theory]
    [InlineData(null, "mocha")]
    [InlineData("", "mocha")]
    [InlineData("mocha", "mocha")]
    [InlineData("MOCHA", "mocha")]
    [InlineData("Mocha", "mocha")]
    [InlineData("latte", "latte")]
    [InlineData("LATTE", "latte")]
    [InlineData("frappe", "frappe")]
    [InlineData("macchiato", "macchiato")]
    [InlineData("invalid", "mocha")]
    [InlineData("dark", "mocha")]
    public void NormalizeThemeName_returns_valid_theme_or_mocha_default(string? input, string expected)
    {
        Assert.Equal(expected, ThemeService.NormalizeThemeName(input));
    }

    [Fact]
    public void AvailableThemes_contains_all_four_palettes()
    {
        Assert.Contains("mocha", ThemeService.AvailableThemes);
        Assert.Contains("frappe", ThemeService.AvailableThemes);
        Assert.Contains("macchiato", ThemeService.AvailableThemes);
        Assert.Contains("latte", ThemeService.AvailableThemes);
        Assert.Equal(4, ThemeService.AvailableThemes.Count);
    }
}
