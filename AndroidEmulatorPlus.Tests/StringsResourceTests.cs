using AndroidEmulatorPlus.Properties;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class StringsResourceTests
{
    [Fact]
    public void All_string_properties_return_nonempty_values()
    {
        var props = typeof(Strings).GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert.True(props.Length > 0, "No string properties found");

        foreach (var prop in props)
        {
            var value = prop.GetValue(null) as string;
            Assert.False(string.IsNullOrEmpty(value),
                $"Strings.{prop.Name} returned null or empty");
        }
    }

    [Fact]
    public void Get_returns_key_name_for_missing_keys()
    {
        var result = Strings.Get("NonExistentKey_12345");
        Assert.Equal("NonExistentKey_12345", result);
    }

    [Fact]
    public void Known_keys_resolve_to_expected_values()
    {
        Assert.Equal("AndroidEmulatorPlus", Strings.AppTitle);
        Assert.Equal("Settings", Strings.SettingsTitle);
        Assert.Equal("AVDs", Strings.TabAvds);
    }
}
