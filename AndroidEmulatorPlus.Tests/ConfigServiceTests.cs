using System.Text.RegularExpressions;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class ConfigServiceTests
{
    [Theory]
    [InlineData("8G", true)]
    [InlineData("16G", true)]
    [InlineData("256G", true)]
    [InlineData("1024G", true)]
    [InlineData("0G", false)]
    [InlineData("G", false)]
    [InlineData("-1G", false)]
    [InlineData("10000G", false)]
    [InlineData("16M", false)]
    [InlineData("8g", true)]
    [InlineData("", false)]
    public void ResizeDisk_size_validation_regex(string size, bool expected)
    {
        var valid = Regex.IsMatch(size, @"^[1-9][0-9]{0,3}G$", RegexOptions.IgnoreCase);
        Assert.Equal(expected, valid);
    }
}
