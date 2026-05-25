using System.IO;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class IniRoundTripTests
{
    [Fact]
    public void ParseIni_skips_comments_and_blank_lines()
    {
        var path = WriteTemp(@"
# This is a comment
; semicolon comment too

hw.ramSize=2048
hw.cpu.ncore=4
disk.dataPartition.size=8G
");
        var dict = AvdService.ParseIni(path);
        Assert.Equal("2048", dict["hw.ramSize"]);
        Assert.Equal("4",    dict["hw.cpu.ncore"]);
        Assert.Equal("8G",   dict["disk.dataPartition.size"]);
        File.Delete(path);
    }

    [Fact]
    public void WriteIni_round_trip_preserves_existing_keys()
    {
        var path = WriteTemp(@"hw.ramSize=2048
hw.cpu.ncore=4
keep.me=yes
");
        AvdService.WriteIni(path, new Dictionary<string, string>
        {
            ["hw.ramSize"] = "4096",
            ["new.key"] = "added",
        });
        var dict = AvdService.ParseIni(path);
        Assert.Equal("4096",  dict["hw.ramSize"]);
        Assert.Equal("4",     dict["hw.cpu.ncore"]);
        Assert.Equal("yes",   dict["keep.me"]);
        Assert.Equal("added", dict["new.key"]);
        File.Delete(path);
    }

    [Fact]
    public void WriteIni_is_case_insensitive_on_keys()
    {
        var path = WriteTemp("hw.ramSize=2048\n");
        AvdService.WriteIni(path, new Dictionary<string, string>
        {
            ["HW.RAMSIZE"] = "8192",
        });
        var dict = AvdService.ParseIni(path);
        // Existing line should be updated, not duplicated.
        var lines = File.ReadAllLines(path);
        Assert.Single(lines, l => l.StartsWith("hw.ramSize=", System.StringComparison.OrdinalIgnoreCase));
        Assert.Equal("8192", dict["hw.ramSize"]);
        File.Delete(path);
    }

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aep-test-{System.Guid.NewGuid():N}.ini");
        File.WriteAllText(path, content.TrimStart('\r', '\n'));
        return path;
    }
}
