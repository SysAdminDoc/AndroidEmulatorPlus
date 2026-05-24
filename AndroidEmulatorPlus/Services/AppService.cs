using AndroidEmulatorPlus.Helpers;
using AndroidEmulatorPlus.Models;

namespace AndroidEmulatorPlus.Services;

public sealed class AppService
{
    private readonly AdbService _adb;
    private readonly LogService _log;

    public AppService(AdbService adb, LogService log)
    {
        _adb = adb;
        _log = log;
    }

    public async Task<List<AndroidApp>> ListAsync(string serial, bool userOnly = true, CancellationToken ct = default)
    {
        var packages = await _adb.ListPackagesAsync(serial, userOnly, ct);
        return packages.Select(p => new AndroidApp { Package = p, IsSystem = !userOnly }).ToList();
    }

    public async Task<ProcessResult> UninstallAsync(string serial, string pkg, bool keepData = false, CancellationToken ct = default)
    {
        _log.Info($"Uninstalling {pkg}{(keepData ? " (keeping data)" : "")}…");
        if (keepData)
        {
            var args = new List<string> { "-s", serial, "uninstall", "-k", pkg };
            return await _adb.RawAsync(args, ct);
        }
        return await _adb.UninstallAsync(serial, pkg, ct);
    }

    public Task<ProcessResult> InstallApkAsync(string serial, string apk, CancellationToken ct = default)
        => _adb.InstallAsync(serial, new[] { apk }, ct);

    /// <summary>Known Google / Samsung / OEM bloat candidates. User reviews and chooses what to remove.</summary>
    public static IReadOnlyList<string> BloatPresetGoogle => new[]
    {
        "com.android.chrome",
        "com.google.android.youtube",
        "com.google.android.apps.youtube.music",
        "com.google.android.apps.maps",
        "com.google.android.apps.podcasts",
        "com.google.android.googlequicksearchbox",
        "com.google.android.apps.docs",
        "com.google.android.apps.photos",
        "com.google.android.apps.tachyon",  // Meet
        "com.google.android.gm",             // Gmail
        "com.google.android.calendar",
    };

    public static IReadOnlyList<string> BloatPresetSamsung => new[]
    {
        "com.samsung.android.bixby.agent",
        "com.samsung.android.bixby.service",
        "com.samsung.android.app.spage",
        "com.samsung.android.app.routines",
        "com.samsung.android.game.gamehome",
        "com.samsung.android.kidsinstaller",
        "com.samsung.android.tvplus",
    };
}
