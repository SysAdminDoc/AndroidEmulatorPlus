using System.IO;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class OrderBaseFirstTests
{
    [Fact]
    public void Returns_input_unchanged_when_singleton()
    {
        var one = new[] { "C:\\tmp\\base.apk" };
        Assert.Equal(one, AppService.OrderBaseFirst(one));
    }

    [Fact]
    public void Picks_literal_base_apk_when_present()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aep-base-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // base.apk smaller than a config split — base must still come first.
            var basePath = Path.Combine(dir, "base.apk");
            var splitPath = Path.Combine(dir, "split_arm64_v8a.apk");
            File.WriteAllBytes(basePath, new byte[100]);
            File.WriteAllBytes(splitPath, new byte[10_000]);
            var ordered = AppService.OrderBaseFirst(new[] { splitPath, basePath });
            Assert.Equal(basePath, ordered[0]);
            Assert.Equal(splitPath, ordered[1]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Falls_back_to_largest_when_no_literal_base()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aep-base-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var pkg = Path.Combine(dir, "com.example.app.apk");
            var splitArm = Path.Combine(dir, "split_arm64.apk");
            var splitDpi = Path.Combine(dir, "split_xxhdpi.apk");
            File.WriteAllBytes(pkg, new byte[10_000]);     // the de-facto base
            File.WriteAllBytes(splitArm, new byte[1_000]);
            File.WriteAllBytes(splitDpi, new byte[2_000]);
            var ordered = AppService.OrderBaseFirst(new[] { splitArm, splitDpi, pkg });
            Assert.Equal(pkg, ordered[0]);
        }
        finally { Directory.Delete(dir, true); }
    }
}
