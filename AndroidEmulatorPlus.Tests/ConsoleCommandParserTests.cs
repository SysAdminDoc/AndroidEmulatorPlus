using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class ConsoleCommandParserTests
{
    [Theory]
    [InlineData("geo fix -122.084 37.422", new[] { "geo", "fix", "-122.084", "37.422" })]
    [InlineData("sms send 5551234 \"Hello, world\"", new[] { "sms", "send", "5551234", "Hello, world" })]
    [InlineData("sms send 5551234 'Hello from AEP'", new[] { "sms", "send", "5551234", "Hello from AEP" })]
    [InlineData("sms send 5551234 \"\"", new[] { "sms", "send", "5551234", "" })]
    [InlineData("network   delay   none", new[] { "network", "delay", "none" })]
    public void ParseEmuArgs_preserves_expected_tokens(string input, string[] expected)
    {
        Assert.Equal(expected, ConsoleService.ParseEmuArgs(input));
    }

    [Fact]
    public void ParseEmuArgs_allows_escaped_quotes_inside_quoted_token()
    {
        Assert.Equal(
            new[] { "sms", "send", "5551234", "Say \"hi\"" },
            ConsoleService.ParseEmuArgs("sms send 5551234 \"Say \\\"hi\\\"\""));
    }

    [Fact]
    public void ParseEmuArgs_rejects_unclosed_quote()
    {
        Assert.Throws<FormatException>(() => ConsoleService.ParseEmuArgs("sms send 5551234 \"unfinished"));
    }
}
