using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class FileNameSanitizationTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("my template", "my_template")]
    public void SanitizeFileName_basic_names(string input, string expected)
    {
        Assert.Equal(expected, AvdTemplateService.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_rejects_path_traversal()
    {
        var result = AvdTemplateService.SanitizeFileName("../../etc/evil");
        Assert.DoesNotContain("../", result);
        Assert.DoesNotContain("..\\", result);
    }

    [Fact]
    public void SanitizeFileName_strips_invalid_filename_chars()
    {
        var result = AvdTemplateService.SanitizeFileName("name:with|stars*");
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("|", result);
        Assert.DoesNotContain("*", result);
    }

    [Fact]
    public void SanitizeFileName_empty_becomes_unnamed()
    {
        Assert.Equal("unnamed", AvdTemplateService.SanitizeFileName(""));
        Assert.Equal("unnamed", AvdTemplateService.SanitizeFileName("   "));
    }

    [Fact]
    public void SanitizeFileName_removes_dotdot_sequences()
    {
        var result = AvdTemplateService.SanitizeFileName("..\\..\\evil");
        Assert.DoesNotContain("..", result);
    }
}
