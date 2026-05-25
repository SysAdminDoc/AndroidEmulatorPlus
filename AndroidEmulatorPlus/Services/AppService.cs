using System.IO;
using System.IO.Compression;
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

    private static string BundleStagingRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "transfer", "bundle-extract");

    /// <summary>
    /// Result of extracting a .apks / .xapk / .apkm archive: list of split APKs to
    /// pass to <c>adb install-multiple</c>, plus any OBB files that should be pushed
    /// into <c>/sdcard/Android/obb/&lt;pkg&gt;/</c>. WorkDir is the temp folder the
    /// caller owns and must delete when done.
    /// </summary>
    public sealed record BundleExtractResult(IReadOnlyList<string> Apks, IReadOnlyList<string> ObbFiles, string? ObbPackage, string WorkDir);

    /// <summary>
    /// Extracts a .apks / .xapk / .apkm archive into a temporary folder. The caller
    /// is responsible for installing the returned APKs (via install-multiple),
    /// pushing the OBBs, and deleting the WorkDir when done.
    /// </summary>
    public BundleExtractResult ExtractBundle(string archivePath)
    {
        Directory.CreateDirectory(BundleStagingRoot);
        var work = Path.Combine(BundleStagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            ZipFile.ExtractToDirectory(archivePath, work);
        }
        catch (Exception ex)
        {
            try { Directory.Delete(work, true); } catch { }
            throw new InvalidOperationException(
                $"Failed to extract {Path.GetFileName(archivePath)}: {ex.Message}", ex);
        }

        var apks = Directory.EnumerateFiles(work, "*.apk", SearchOption.AllDirectories)
            .OrderBy(static p => p.Length) // smallest first; the base APK is typically smallest after splits
            .ToList();
        var obbs = Directory.EnumerateFiles(work, "*.obb", SearchOption.AllDirectories)
            .ToList();

        if (apks.Count == 0)
        {
            try { Directory.Delete(work, true); } catch { }
            throw new InvalidOperationException(
                $"No APKs found inside {Path.GetFileName(archivePath)}. The bundle may be corrupted.");
        }

        // .xapk and .apkm often ship a manifest.json with the package name. Try to read it
        // so the OBB push knows where to land.
        string? obbPkg = TryReadBundleManifestPackage(work);

        // Fallback: derive package name from any .obb filename of the form main.<ver>.<pkg>.obb.
        if (obbPkg is null && obbs.Count > 0)
        {
            foreach (var obb in obbs)
            {
                var parts = Path.GetFileNameWithoutExtension(obb).Split('.');
                if (parts.Length >= 3 && parts[0] is "main" or "patch")
                {
                    obbPkg = string.Join('.', parts.Skip(2));
                    break;
                }
            }
        }

        _log.Info($"Bundle extracted: {apks.Count} APK(s){(obbs.Count > 0 ? $", {obbs.Count} OBB(s) → {obbPkg ?? "<unknown package>"}" : "")}");
        return new BundleExtractResult(apks, obbs, obbPkg, work);
    }

    /// <summary>
    /// Looks for a manifest.json / info.json in the extracted folder and reads the
    /// package_name field. Used by SAI exports (.apks), APKMirror bundles (.apkm),
    /// and a few .xapk producers.
    /// </summary>
    private static string? TryReadBundleManifestPackage(string workDir)
    {
        foreach (var fname in new[] { "manifest.json", "info.json", "icon.png" })
        {
            // skip pngs, just listing files to keep search small
            if (!fname.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var path in Directory.EnumerateFiles(workDir, fname, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var key in new[] { "package_name", "packageName", "package", "pkg" })
                    {
                        if (doc.RootElement.TryGetProperty(key, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
                            return prop.GetString();
                    }
                }
                catch { /* ignored */ }
            }
        }
        return null;
    }

    /// <summary>Pushes a single OBB into <c>/sdcard/Android/obb/&lt;pkg&gt;/&lt;name&gt;</c>.</summary>
    public async Task<bool> PushObbAsync(string serial, string pkg, string localObb, CancellationToken ct = default)
    {
        var remoteDir = $"/sdcard/Android/obb/{pkg}";
        await _adb.ShellAsync(serial, $"mkdir -p {remoteDir}", ct);
        var remote = $"{remoteDir}/{Path.GetFileName(localObb)}";
        var r = await _adb.PushAsync(serial, localObb, remote, ct);
        return r.Success;
    }

    /// <summary>
    /// Returns the union of selected package classes, each row tagged with the right
    /// system / disabled flag. The flags only widen the listing — at minimum the
    /// third-party (-3) set is always included.
    /// </summary>
    public async Task<List<AndroidApp>> ListAsync(string serial, bool includeSystem, bool includeDisabled, CancellationToken ct = default)
    {
        var third = await _adb.ListPackagesFlagAsync(serial, "-3", ct);
        var system = includeSystem ? await _adb.ListPackagesFlagAsync(serial, "-s", ct) : new HashSet<string>(StringComparer.Ordinal);
        var disabled = await _adb.ListPackagesFlagAsync(serial, "-d", ct);

        var all = new HashSet<string>(third, StringComparer.Ordinal);
        if (includeSystem) all.UnionWith(system);
        if (includeDisabled) all.UnionWith(disabled);

        return all.OrderBy(static p => p).Select(p => new AndroidApp
        {
            Package = p,
            IsSystem = system.Contains(p),
            IsDisabled = disabled.Contains(p),
        }).ToList();
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

    /// <summary>
    /// Per-user uninstall via <c>pm uninstall --user 0</c>. Preinstalled OEM apps marked
    /// as system reject plain <c>adb uninstall</c>; the per-user form removes the app
    /// from user 0 (survives reboot, undone by <see cref="ReinstallExistingAsync"/>
    /// or factory reset). No root required.
    /// </summary>
    public async Task<ProcessResult> UninstallForUser0Async(string serial, string pkg, CancellationToken ct = default)
    {
        _log.Info($"Disabling {pkg} for user 0 (pm uninstall --user 0)…");
        var r = await _adb.ShellAsync(serial, $"pm uninstall --user 0 {pkg}", ct);
        return r;
    }

    /// <summary>Reverse of <see cref="UninstallForUser0Async"/>: reinstalls the system APK for user 0.</summary>
    public async Task<ProcessResult> ReinstallExistingAsync(string serial, string pkg, CancellationToken ct = default)
    {
        _log.Info($"Re-enabling {pkg} for user 0…");
        return await _adb.ShellAsync(serial, $"cmd package install-existing {pkg}", ct);
    }

    public Task<ProcessResult> InstallApkAsync(string serial, string apk, CancellationToken ct = default)
        => _adb.InstallAsync(serial, new[] { apk }, ct);

    /// <summary>Calls <c>adb install-multiple</c> with a list of split APKs.</summary>
    public Task<ProcessResult> InstallSplitApksAsync(string serial, IEnumerable<string> apks, CancellationToken ct = default)
        => _adb.InstallAsync(serial, apks, ct);

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
