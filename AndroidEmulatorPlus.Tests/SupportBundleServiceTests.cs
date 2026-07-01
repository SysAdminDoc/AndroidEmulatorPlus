using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class SupportBundleServiceTests
{
    [Fact]
    public void Redact_masks_windows_user_paths()
    {
        var input = @"Error at C:\Users\JohnDoe\AppData\Local\AndroidEmulatorPlus\crash.log";
        var result = SupportBundleService.Redact(input);
        Assert.DoesNotContain("JohnDoe", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Redact_masks_linux_home_paths()
    {
        var input = "/home/johndoe/.android/avd/test.avd";
        var result = SupportBundleService.Redact(input);
        Assert.DoesNotContain("johndoe", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Redact_masks_ip_addresses()
    {
        var input = "adb connect 192.168.1.42:5555";
        var result = SupportBundleService.Redact(input);
        Assert.DoesNotContain("192.168.1.42", result);
        Assert.Contains("[REDACTED_IP]", result);
    }

    [Fact]
    public void Redact_preserves_non_sensitive_content()
    {
        var input = "SDK version 36.0.0 loaded successfully";
        var result = SupportBundleService.Redact(input);
        Assert.Equal(input, result);
    }
}
