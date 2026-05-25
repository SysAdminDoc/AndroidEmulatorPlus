using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class AdbShellSafetyTests
{
    [Theory]
    [InlineData("", "''")]
    [InlineData("plain", "'plain'")]
    [InlineData("has spaces", "'has spaces'")]
    [InlineData("abc'def", "'abc'\\''def'")]
    public void ShellQuote_uses_single_quote_safe_shell_form(string input, string expected)
    {
        Assert.Equal(expected, AdbService.ShellQuote(input));
    }

    [Theory]
    [InlineData("com.example.app")]
    [InlineData("a.b_c.D2")]
    [InlineData("package")]
    public void IsSafeAndroidPackageName_accepts_valid_names(string value)
    {
        Assert.True(AdbService.IsSafeAndroidPackageName(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".com.example")]
    [InlineData("com..example")]
    [InlineData("com.example-evil")]
    [InlineData("com.example;rm -rf /")]
    public void IsSafeAndroidPackageName_rejects_shell_and_path_syntax(string value)
    {
        Assert.False(AdbService.IsSafeAndroidPackageName(value));
    }

    [Theory]
    [InlineData("zygisk-detach")]
    [InlineData("PlayIntegrityFork")]
    [InlineData("foo.bar_1")]
    public void IsSafeMagiskModuleId_accepts_catalog_ids(string value)
    {
        Assert.True(AdbService.IsSafeMagiskModuleId(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../module")]
    [InlineData("module/id")]
    [InlineData("module;touch /data/local/tmp/pwned")]
    public void IsSafeMagiskModuleId_rejects_path_and_shell_syntax(string value)
    {
        Assert.False(AdbService.IsSafeMagiskModuleId(value));
    }
}
