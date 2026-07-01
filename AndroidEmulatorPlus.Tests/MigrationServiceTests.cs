using System.Reflection;
using System.Formats.Tar;
using System.IO;
using AndroidEmulatorPlus.Helpers;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class MigrationServiceTests
{
    private static string ParseFailReason(string input)
    {
        var mi = typeof(MigrationService).GetMethod("ParseFailReason",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)mi.Invoke(null, new object?[] { input })!;
    }

    [Fact]
    public void Returns_INSTALL_FAILED_token_when_present()
    {
        var line = "[INSTALL_FAILED_INSUFFICIENT_STORAGE: not enough space]";
        Assert.Equal("INSTALL_FAILED_INSUFFICIENT_STORAGE", ParseFailReason(line));
    }

    [Fact]
    public void Returns_INSTALL_FAILED_token_inline_with_other_text()
    {
        var line = "Performing Streamed Install\nINSTALL_FAILED_VERSION_DOWNGRADE\n";
        Assert.Equal("INSTALL_FAILED_VERSION_DOWNGRADE", ParseFailReason(line));
    }

    [Fact]
    public void Fallback_when_no_known_token()
    {
        Assert.Equal("install failed", ParseFailReason("Cleanup deleted temp paths"));
    }

    [Theory]
    [InlineData("pull")]
    [InlineData("unsafe")]
    [InlineData("push")]
    [InlineData("uid")]
    [InlineData("extract")]
    public async Task TransferInternalData_CleansRemoteTars_OnFailurePaths(string failure)
    {
        var adb = new FakeAdb { Failure = failure };
        var svc = new MigrationService(adb, new LogService());

        var result = await svc.TransferInternalDataAsync("phone", "emu", "com.example.app", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(("phone", "/sdcard/com.example.app.tar", true), adb.Cleanup);
        Assert.Contains(("emu", "/sdcard/com.example.app.tar", true), adb.Cleanup);
    }

    [Fact]
    public async Task TransferInternalData_CleansRemoteTars_WhenCancelled()
    {
        var adb = new FakeAdb { Failure = "cancel" };
        var svc = new MigrationService(adb, new LogService());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            svc.TransferInternalDataAsync("phone", "emu", "com.example.app", new CancellationToken(canceled: true)));

        Assert.Contains(("phone", "/sdcard/com.example.app.tar", true), adb.Cleanup);
        Assert.Contains(("emu", "/sdcard/com.example.app.tar", true), adb.Cleanup);
    }

    [Fact]
    public void BuildReceipt_tracks_per_package_legs_and_failures()
    {
        var packages = new List<MigrationPackageReceipt>
        {
            new()
            {
                Package = "com.example.ok",
                Success = true,
                Legs = [
                    new() { Leg = "apk", Success = true, SizeBytes = 1000, Detail = "1 apk" },
                    new() { Leg = "internal", Success = true, SizeBytes = 5000, Detail = "uid 10001" },
                ],
            },
            new()
            {
                Package = "com.example.fail",
                Success = false,
                Legs = [
                    new() { Leg = "apk", Success = true, SizeBytes = 2000, Detail = "1 apk" },
                    new() { Leg = "internal", Success = false, SizeBytes = 0, Detail = "tar failed" },
                ],
            },
        };

        var receipt = MigrationService.BuildReceipt("phone1", "emu1", ["apk", "internal"], packages, cancelled: false);

        Assert.Equal("phone1", receipt.SourceSerial);
        Assert.Equal("emu1", receipt.TargetSerial);
        Assert.Equal(2, receipt.Packages.Count);
        Assert.Equal(1, receipt.SuccessCount);
        Assert.Equal(1, receipt.FailCount);
        Assert.Equal(8000, receipt.TotalBytes);
        Assert.False(receipt.Cancelled);
        Assert.Single(receipt.FailedPackages);
        Assert.Equal("com.example.fail", receipt.FailedPackages[0]);
    }

    [Fact]
    public void BuildReceipt_cancelled_flag()
    {
        var receipt = MigrationService.BuildReceipt("p", "e", ["apk"], [], cancelled: true);
        Assert.True(receipt.Cancelled);
    }

    [Fact]
    public async Task DryRun_blocks_when_phone_not_rooted_and_internal_selected()
    {
        var adb = new FakeAdb { PhoneRooted = false, EmuRooted = true };
        var svc = new MigrationService(adb, new LogService());

        var result = await svc.DryRunAsync("phone", "emu", ["com.example.app"],
            doApk: true, doInternal: true, doExternal: false, doObb: false,
            forceDataForNoBackup: false, CancellationToken.None);

        Assert.False(result.CanProceed);
        Assert.Contains(result.Blockers, b => b.Contains("Phone is not rooted"));
    }

    [Fact]
    public async Task DryRun_passes_when_apk_only()
    {
        var adb = new FakeAdb { PhoneRooted = false, EmuRooted = false };
        var svc = new MigrationService(adb, new LogService());

        var result = await svc.DryRunAsync("phone", "emu", ["com.example.app"],
            doApk: true, doInternal: false, doExternal: false, doObb: false,
            forceDataForNoBackup: false, CancellationToken.None);

        Assert.True(result.CanProceed);
        Assert.Equal(1, result.TotalPackages);
    }

    private sealed class FakeAdb : AdbService
    {
        public FakeAdb() : base(new SdkLocator(), new LogService()) { }

        public string Failure { get; set; } = "";
        public bool PhoneRooted { get; set; } = true;
        public bool EmuRooted { get; set; } = true;
        public List<(string serial, string remote, bool root)> Cleanup { get; } = new();

        public override Task<ProcessResult> ShellAsync(string serial, string command, CancellationToken ct = default)
        {
            if (command.Contains("rm -f ", StringComparison.Ordinal))
            {
                Cleanup.Add((serial, ExtractQuotedRemote(command), false));
                return Ok();
            }
            if (command.Contains("tar --help", StringComparison.Ordinal)) return Task.FromResult(new ProcessResult(0, "exclude\n", ""));
            if (command.Contains("stat -c %s", StringComparison.Ordinal)) return Task.FromResult(new ProcessResult(0, "2048\n", ""));
            if (command.Contains("am force-stop", StringComparison.Ordinal)) return Ok();
            if (command.Contains("pm path", StringComparison.Ordinal)) return Task.FromResult(new ProcessResult(0, "package:/data/app/com.example.app/base.apk\n", ""));
            if (command.Contains("pm dump", StringComparison.Ordinal)) return Task.FromResult(new ProcessResult(0, "flags=[ ALLOW_BACKUP ]\n", ""));
            if (command.Contains("df /data", StringComparison.Ordinal)) return Task.FromResult(new ProcessResult(0, "1048576\n", ""));
            return Ok();
        }

        public override Task<ProcessResult> RootShellAsync(string serial, string command, CancellationToken ct = default)
        {
            if (command.Contains("rm -f ", StringComparison.Ordinal))
            {
                Cleanup.Add((serial, ExtractQuotedRemote(command), true));
                return Ok();
            }
            if (command.Contains("id", StringComparison.Ordinal) && !command.Contains("tar") && !command.Contains("stat"))
            {
                var rooted = serial == "phone" ? PhoneRooted : EmuRooted;
                return Task.FromResult(rooted
                    ? new ProcessResult(0, "uid=0(root)\n", "")
                    : new ProcessResult(1, "", "Permission denied"));
            }
            if (command.Contains("du -sb", StringComparison.Ordinal))
                return Task.FromResult(new ProcessResult(0, "4096\n", ""));
            if (command.Contains("tar --exclude", StringComparison.Ordinal)) return Ok();
            if (command.Contains("stat -c %u", StringComparison.Ordinal))
                return Task.FromResult(Failure == "uid"
                    ? new ProcessResult(1, "", "missing")
                    : new ProcessResult(0, "10001\n", ""));
            if (command.Contains("tar -xf", StringComparison.Ordinal))
                return Task.FromResult(Failure == "extract"
                    ? new ProcessResult(1, "", "extract failed")
                    : new ProcessResult(0, "OK\n", ""));
            return Ok();
        }

        public override Task<ProcessResult> PullAsync(string serial, string remote, string local, CancellationToken ct = default)
        {
            if (Failure == "cancel") throw new OperationCanceledException();
            if (Failure == "pull") return Task.FromResult(new ProcessResult(1, "", "pull failed"));
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            WriteDataTar(local, Failure == "unsafe" ? "com.other.app/files/prefs.xml" : "com.example.app/files/prefs.xml");
            return Ok();
        }

        public override Task<ProcessResult> PushAsync(string serial, string local, string remote, CancellationToken ct = default)
            => Task.FromResult(Failure == "push"
                ? new ProcessResult(1, "", "push failed")
                : new ProcessResult(0, "ok", ""));

        private static Task<ProcessResult> Ok() => Task.FromResult(new ProcessResult(0, "ok\n", ""));

        private static string ExtractQuotedRemote(string command)
        {
            var marker = "rm -f ";
            var value = command[(command.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..].Trim();
            return value.Trim('\'');
        }

        private static void WriteDataTar(string path, string entryName)
        {
            using var fs = File.Create(path);
            using var writer = new TarWriter(fs, TarEntryFormat.Pax, leaveOpen: false);
            var bytes = System.Text.Encoding.UTF8.GetBytes("data");
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entryName)
            {
                DataStream = new MemoryStream(bytes),
            });
        }
    }
}
