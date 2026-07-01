using System.Text.RegularExpressions;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

/// <summary>
/// Verifies the parsing logic MigrationService.AllowsBackupAsync uses against
/// several known shapes of `pm dump` output. The service itself runs against
/// a real adb shell, so we test the regex/string match directly.
/// </summary>
public class AllowBackupParsingTests
{
    private static bool Parse(string pmDump)
    {
        foreach (var line in pmDump.Split('\n'))
        {
            if (line.Contains("flags=", System.StringComparison.OrdinalIgnoreCase)
                && line.Contains('[', System.StringComparison.Ordinal))
            {
                return line.Contains("ALLOW_BACKUP", System.StringComparison.Ordinal);
            }
        }
        var m = Regex.Match(pmDump, @"allowBackup\s*=\s*(true|false)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
        return true;
    }

    [Fact]
    public void Flags_line_with_ALLOW_BACKUP_returns_true()
    {
        var dump = @"
Packages:
  Package [com.example.app] (1234):
    userId=10100
    flags=[ ALLOW_BACKUP ALLOW_CLEAR_USER_DATA HAS_CODE ]
";
        Assert.True(Parse(dump));
    }

    [Fact]
    public void Flags_line_without_ALLOW_BACKUP_returns_false_when_followed_by_allowBackup_equals()
    {
        var dump = @"
Packages:
  Package [com.example.banking] (1234):
    flags=[ HAS_CODE ]
    allowBackup=false
";
        Assert.False(Parse(dump));
    }

    [Fact]
    public void Flags_line_without_ALLOW_BACKUP_returns_false_standalone()
    {
        var dump = @"
Packages:
  Package [com.example.banking] (1234):
    flags=[ HAS_CODE ALLOW_CLEAR_USER_DATA ]
";
        Assert.False(Parse(dump));
    }

    [Fact]
    public void Newer_aosp_allowBackup_true_explicit()
    {
        var dump = "allowBackup=true";
        Assert.True(Parse(dump));
    }

    [Fact]
    public void Empty_dump_assumes_true()
    {
        // Conservative default: when we can't tell, run the migration.
        Assert.True(Parse(""));
    }
}
