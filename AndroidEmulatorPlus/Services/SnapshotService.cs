using System.IO;
using System.Text.RegularExpressions;
using AndroidEmulatorPlus.Helpers;
using AndroidEmulatorPlus.Models;

namespace AndroidEmulatorPlus.Services;

public sealed record Snapshot(string Name, string Folder, long SizeBytes, DateTime Modified)
{
    public bool IsDefaultBoot => Name.Equals("default_boot", StringComparison.OrdinalIgnoreCase);
    public string Display => SizeBytes < 1024L * 1024
        ? $"{Name}  ({SizeBytes / 1024} KB · {Modified:yyyy-MM-dd HH:mm})"
        : $"{Name}  ({SizeBytes / 1024 / 1024} MB · {Modified:yyyy-MM-dd HH:mm})";
}

/// <summary>
/// Reads / loads / saves / deletes AVD snapshots in
/// <c>~/.android/avd/&lt;name&gt;.avd/snapshots/&lt;snapshotName&gt;/</c>.
///
/// The emulator console (`telnet localhost 5554`) accepts <c>avd snapshot save</c> /
/// <c>avd snapshot load</c> / <c>avd snapshot del</c> on a running emulator. Listing
/// is done off-disk because it works whether the emulator is running or not.
/// </summary>
public sealed class SnapshotService
{
    private readonly SdkLocator _sdk;
    private readonly AdbService _adb;
    private readonly LogService _log;

    public SnapshotService(SdkLocator sdk, AdbService adb, LogService log)
    {
        _sdk = sdk;
        _adb = adb;
        _log = log;
    }

    public static bool IsSafeSnapshotName(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 128
           && Regex.IsMatch(value, @"^[A-Za-z0-9._ -]+$");

    public List<Snapshot> List(string avdName)
    {
        var list = new List<Snapshot>();
        if (!AvdService.IsSafeAvdName(avdName)) return list;
        if (_sdk.AvdHome is null) return list;
        var snapsDir = Path.Combine(_sdk.AvdHome, avdName + ".avd", "snapshots");
        if (!Directory.Exists(snapsDir)) return list;

        foreach (var dir in Directory.EnumerateDirectories(snapsDir))
        {
            long size = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    try { size += new FileInfo(f).Length; } catch { }
            }
            catch { }
            DateTime mod;
            try { mod = Directory.GetLastWriteTime(dir); } catch { mod = DateTime.MinValue; }
            list.Add(new Snapshot(Path.GetFileName(dir), dir, size, mod));
        }
        return list.OrderByDescending(s => s.Modified).ToList();
    }

    /// <summary>
    /// Deletes a snapshot folder. Safe whether the emulator is running or not, but
    /// loading from a freshly-deleted snapshot will obviously fail.
    /// </summary>
    public bool Delete(string avdName, string snapshotName)
    {
        if (!AvdService.IsSafeAvdName(avdName) || !IsSafeSnapshotName(snapshotName)) return false;
        if (_sdk.AvdHome is null) return false;
        var dir = Path.Combine(_sdk.AvdHome, avdName + ".avd", "snapshots", snapshotName);
        if (!Directory.Exists(dir)) return false;
        try
        {
            Directory.Delete(dir, recursive: true);
            _log.Info($"Deleted snapshot '{snapshotName}' for AVD '{avdName}'.");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Snapshot delete failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Saves a snapshot via <c>adb emu avd snapshot save &lt;name&gt;</c>. Requires the
    /// emulator to be running.
    /// </summary>
    public async Task<bool> SaveAsync(string serial, string snapshotName, CancellationToken ct = default)
    {
        if (!IsSafeSnapshotName(snapshotName))
        {
            _log.Error("Snapshot save skipped: invalid snapshot name.");
            return false;
        }
        var r = await ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "-s", serial, "emu", "avd", "snapshot", "save", snapshotName },
            extraEnv: new Dictionary<string, string?>
            {
                ["MSYS_NO_PATHCONV"] = "1",
                ["MSYS2_ARG_CONV_EXCL"] = "*",
            }, ct: ct);
        if (r.Success) _log.Success($"Snapshot '{snapshotName}' saved on {serial}.");
        else _log.Error("Snapshot save: " + r.Combined.Trim());
        return r.Success;
    }

    /// <summary>Loads a snapshot via <c>adb emu avd snapshot load &lt;name&gt;</c>.</summary>
    public async Task<bool> LoadAsync(string serial, string snapshotName, CancellationToken ct = default)
    {
        if (!IsSafeSnapshotName(snapshotName))
        {
            _log.Error("Snapshot load skipped: invalid snapshot name.");
            return false;
        }
        var r = await ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "-s", serial, "emu", "avd", "snapshot", "load", snapshotName },
            extraEnv: new Dictionary<string, string?>
            {
                ["MSYS_NO_PATHCONV"] = "1",
                ["MSYS2_ARG_CONV_EXCL"] = "*",
            }, ct: ct);
        if (r.Success) _log.Success($"Snapshot '{snapshotName}' loaded on {serial}.");
        else _log.Error("Snapshot load: " + r.Combined.Trim());
        return r.Success;
    }
}
