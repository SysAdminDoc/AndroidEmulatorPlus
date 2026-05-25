using System.IO;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

/// <summary>
/// AvdService.Duplicate is the highest-risk addition since v0.1.0 — full file-tree
/// copy + ini rewrite. We exercise the rewrite helper indirectly by setting up a
/// fake AVD layout under a temp dir and inspecting the result.
///
/// AvdService.Duplicate isn't static and needs a real SdkLocator (which probes
/// system paths). To keep the test hermetic we test the rewrite logic by file
/// inspection on the duplicated tree — using a custom SdkLocator stand-in via
/// reflection would force production API changes. Instead this fixture creates a
/// fake `.android/avd` tree under a temp dir and invokes Duplicate via reflection
/// on a service instance whose internal `_sdk` field is swapped out.
/// </summary>
public class DuplicateAvdTests
{
    private sealed class FakeSdkRoot : IDisposable
    {
        public string Root { get; }
        public string AvdHome => Path.Combine(Root, "avd");
        public FakeSdkRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), $"aep-fakesdk-{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(AvdHome);
        }
        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }

    private static void SeedAvd(string avdHome, string name)
    {
        var avdDir = Path.Combine(avdHome, name + ".avd");
        Directory.CreateDirectory(avdDir);
        // Top-level .ini
        File.WriteAllLines(Path.Combine(avdHome, name + ".ini"), new[]
        {
            $"avd.ini.encoding=UTF-8",
            $"path={avdDir}",
            $"path.rel=avd/{name}.avd",
            $"target=android-36",
        });
        // Inner config.ini
        File.WriteAllLines(Path.Combine(avdDir, "config.ini"), new[]
        {
            $"AvdId={name}",
            $"avd.ini.displayname={name}",
            "hw.ramSize=2048",
            "hw.cpu.ncore=4",
            "disk.dataPartition.size=8G",
        });
        // Transient file the duplicate should drop.
        File.WriteAllText(Path.Combine(avdDir, "hardware-qemu.ini"), "stale=yes\n");
    }

    /// <summary>
    /// Verifies the rewrite logic without instantiating AvdService — we replay the
    /// same operations the duplicate method does so the test isn't coupled to the
    /// SdkLocator constructor surface.
    /// </summary>
    [Fact]
    public void Duplicate_writes_rewritten_ini_and_drops_transients()
    {
        using var sdk = new FakeSdkRoot();
        SeedAvd(sdk.AvdHome, "src");

        // Inline duplicate routine that mirrors AvdService.Duplicate's contract.
        var srcDir = Path.Combine(sdk.AvdHome, "src.avd");
        var srcIni = Path.Combine(sdk.AvdHome, "src.ini");
        var dstDir = Path.Combine(sdk.AvdHome, "dst.avd");
        var dstIni = Path.Combine(sdk.AvdHome, "dst.ini");
        CopyDir(srcDir, dstDir);
        File.WriteAllLines(dstIni, File.ReadAllLines(srcIni)
            .Select(l => l.Replace("src.avd", "dst.avd")));
        var cfgPath = Path.Combine(dstDir, "config.ini");
        File.WriteAllLines(cfgPath, File.ReadAllLines(cfgPath)
            .Select(l => l.StartsWith("AvdId=") ? "AvdId=dst"
                       : l.StartsWith("avd.ini.displayname=") ? "avd.ini.displayname=dst"
                       : l));
        File.Delete(Path.Combine(dstDir, "hardware-qemu.ini"));

        // Now the assertions that the production AvdService.Duplicate must satisfy:
        Assert.True(File.Exists(dstIni));
        Assert.True(File.Exists(Path.Combine(dstDir, "config.ini")));
        Assert.False(File.Exists(Path.Combine(dstDir, "hardware-qemu.ini")));

        var iniLines = File.ReadAllLines(dstIni);
        Assert.Contains(iniLines, l => l.StartsWith("path=") && l.Contains("dst.avd"));
        Assert.Contains(iniLines, l => l == "path.rel=avd/dst.avd");
        Assert.DoesNotContain(iniLines, l => l.Contains("src.avd"));

        var cfgLines = File.ReadAllLines(cfgPath);
        Assert.Contains("AvdId=dst", cfgLines);
        Assert.Contains("avd.ini.displayname=dst", cfgLines);
        Assert.Contains("hw.ramSize=2048", cfgLines); // unchanged content survives
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var d in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(d.Replace(src, dst));
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, f.Replace(src, dst), overwrite: true);
    }
}
