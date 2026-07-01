using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using AndroidEmulatorPlus.Helpers;
using AndroidEmulatorPlus.Models;

namespace AndroidEmulatorPlus.Services;

public sealed class AppService
{
    private const int DefaultZipMaxEntries = 20_000;
    private const long DefaultZipMaxEntryUncompressedBytes = 8L * 1024 * 1024 * 1024;
    private const long DefaultZipMaxTotalUncompressedBytes = 32L * 1024 * 1024 * 1024;
    private const double DefaultZipMaxCompressionRatio = 1_000d;

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
        if (!TryValidateZipForExtraction(archivePath, out var zipDetail))
            throw new InvalidOperationException(
                $"Bundle archive rejected before extraction: {zipDetail}");

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

        // C-02 ordering: the base APK must come first to `adb install-multiple`. The
        // base owns AndroidManifest.xml and most resources, so it is usually the
        // *largest* of the inner APKs (per-config splits trim down). The base
        // filename is typically literally `base.apk` (SAI / bundletool convention)
        // but third-party builders sometimes name it `<pkg>.apk`. Selection rule:
        // 1. If an entry named `base.apk` exists, take it.
        // 2. Otherwise treat the largest APK as the base.
        // 3. Splits follow in any order.
        var allApks = Directory.EnumerateFiles(work, "*.apk", SearchOption.AllDirectories).ToList();
        var apks = OrderBaseFirst(allApks);
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
        if (obbPkg is not null && !AdbService.IsSafeAndroidPackageName(obbPkg))
        {
            _log.Warning($"Bundle declared an invalid package name for OBB push: {obbPkg}. Skipping OBB push.");
            obbPkg = null;
        }

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
    /// Returns the inner APKs in base-first install-multiple order. Public for tests.
    /// </summary>
    public static List<string> OrderBaseFirst(IEnumerable<string> apkPaths)
    {
        var list = apkPaths.ToList();
        if (list.Count <= 1) return list;
        // 1. Prefer an entry literally named base.apk (case-insensitive).
        var named = list.FirstOrDefault(p =>
            Path.GetFileName(p).Equals("base.apk", StringComparison.OrdinalIgnoreCase));
        // 2. Otherwise fall back to the largest file as the base candidate.
        var fallback = named ?? list.OrderByDescending(static p =>
        {
            try { return new FileInfo(p).Length; } catch { return 0L; }
        }).First();
        var rest = list.Where(p => !string.Equals(p, fallback, StringComparison.OrdinalIgnoreCase));
        return new[] { fallback }.Concat(rest).ToList();
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
        if (!AdbService.IsSafeAndroidPackageName(pkg))
        {
            _log.Error("OBB push skipped: invalid package name in bundle metadata.");
            return false;
        }
        var remoteDir = $"/sdcard/Android/obb/{pkg}";
        await _adb.ShellAsync(serial, $"mkdir -p {AdbService.ShellQuote(remoteDir)}", ct);
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
        if (!AdbService.IsSafeAndroidPackageName(pkg))
            return new ProcessResult(-1, "", "invalid package name");
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
        if (!AdbService.IsSafeAndroidPackageName(pkg)) return new ProcessResult(-1, "", "invalid package name");
        _log.Info($"Disabling {pkg} for user 0 (pm uninstall --user 0)…");
        var r = await _adb.ShellAsync(serial, $"pm uninstall --user 0 {AdbService.ShellQuote(pkg)}", ct);
        return r;
    }

    /// <summary>Reverse of <see cref="UninstallForUser0Async"/>: reinstalls the system APK for user 0.</summary>
    public async Task<ProcessResult> ReinstallExistingAsync(string serial, string pkg, CancellationToken ct = default)
    {
        if (!AdbService.IsSafeAndroidPackageName(pkg)) return new ProcessResult(-1, "", "invalid package name");
        _log.Info($"Re-enabling {pkg} for user 0…");
        return await _adb.ShellAsync(serial, $"cmd package install-existing {AdbService.ShellQuote(pkg)}", ct);
    }

    public Task<ProcessResult> InstallApkAsync(string serial, string apk, CancellationToken ct = default)
        => _adb.InstallAsync(serial, new[] { apk }, ct);

    /// <summary>Calls <c>adb install-multiple</c> with a list of split APKs.</summary>
    public Task<ProcessResult> InstallSplitApksAsync(string serial, IEnumerable<string> apks, CancellationToken ct = default)
        => _adb.InstallAsync(serial, apks, ct);

    // Preset bloat lists moved to Resources/bloat-presets.json + PresetService (B-04).

    /// <summary>
    /// R-05: Export /data/data/&lt;pkg&gt; from the emulator into a local .zip. Requires
    /// root on the emulator. The zip contains a single top-level entry: the package's
    /// data tarball, plus a metadata.json with the package name and emulator UID.
    /// </summary>
    public async Task<bool> ExportAppDataAsync(string serial, string pkg, string destZip, CancellationToken ct = default)
    {
        if (!AdbService.IsSafeAndroidPackageName(pkg))
        {
            _log.Error("Export skipped: invalid package name.");
            return false;
        }
        var staging = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidEmulatorPlus", "transfer", $"export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var remoteTar = $"/sdcard/aep-export-{pkg}.tar";
        try
        {
            var tar = await _adb.RootShellAsync(serial,
                $"cd /data/data && tar -cf {AdbService.ShellQuote(remoteTar)} {AdbService.ShellQuote(pkg)} && chmod 666 {AdbService.ShellQuote(remoteTar)}", ct);
            if (!tar.Success) { _log.Error("export tar failed: " + tar.Combined.Trim()); return false; }
            var localTar = Path.Combine(staging, $"{pkg}.tar");
            var pull = await _adb.PullAsync(serial, remoteTar, localTar, ct);
            if (!pull.Success || !File.Exists(localTar)) { _log.Error("export pull failed."); return false; }

            // Sidecar metadata so Import can restore against a different UID.
            var uidR = await _adb.RootShellAsync(serial, $"stat -c %u {AdbService.ShellQuote($"/data/data/{pkg}")}", ct);
            var uid = int.TryParse(uidR.StdOut.Trim(), out var u) ? u : -1;
            var meta = $$"""
                { "package": "{{pkg}}", "uid": {{uid}}, "exported": "{{DateTime.UtcNow:o}}", "version": 1 }
                """;
            File.WriteAllText(Path.Combine(staging, "metadata.json"), meta);

            try { if (File.Exists(destZip)) File.Delete(destZip); } catch { }
            ZipFile.CreateFromDirectory(staging, destZip);
            _log.Success($"Exported {pkg} → {destZip} ({new FileInfo(destZip).Length / 1024 / 1024} MB).");
            return true;
        }
        finally
        {
            try { await _adb.RootShellAsync(serial, $"rm -f {AdbService.ShellQuote(remoteTar)}", ct); } catch { }
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    /// <summary>
    /// R-05: Restore a .zip produced by <see cref="ExportAppDataAsync"/> back into
    /// /data/data/&lt;pkg&gt; on the emulator (root required). The metadata.json's UID is
    /// re-mapped to whatever uid the package currently has on the target.
    /// </summary>
    public async Task<bool> ImportAppDataAsync(string serial, string zipPath, CancellationToken ct = default)
    {
        var staging = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidEmulatorPlus", "transfer", $"import-{Guid.NewGuid():N}");
        string? remoteTar = null;
        try
        {
            if (!TryValidateZipForExtraction(zipPath, out var zipDetail))
            {
                _log.Error("Import zip rejected before extraction: " + zipDetail);
                return false;
            }

            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging);
            var metaPath = Path.Combine(staging, "metadata.json");
            if (!File.Exists(metaPath)) { _log.Error("Import: metadata.json missing in zip."); return false; }
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metaPath));
            var pkg = doc.RootElement.GetProperty("package").GetString();
            if (string.IsNullOrEmpty(pkg)) { _log.Error("Import: package field missing."); return false; }
            if (!AdbService.IsSafeAndroidPackageName(pkg)) { _log.Error("Import: invalid package field in metadata.json."); return false; }

            var tars = Directory.EnumerateFiles(staging, "*.tar").ToList();
            if (tars.Count == 0) { _log.Error("Import: no tar inside zip."); return false; }
            if (tars.Count > 1) { _log.Error("Import: expected exactly one tar inside zip."); return false; }
            var tar = tars[0];
            if (!TryValidateDataTarForImport(tar, pkg, out var tarDetail))
            {
                _log.Error("Import: unsafe tar archive: " + tarDetail);
                return false;
            }

            remoteTar = $"/sdcard/aep-import-{pkg}.tar";
            var push = await _adb.PushAsync(serial, tar, remoteTar, ct);
            if (!push.Success) { _log.Error("Import push failed."); return false; }

            await _adb.ShellAsync(serial, $"am force-stop {AdbService.ShellQuote(pkg)}", ct);
            var uidR = await _adb.RootShellAsync(serial, $"stat -c %u {AdbService.ShellQuote($"/data/data/{pkg}")}", ct);
            if (!int.TryParse(uidR.StdOut.Trim(), out var uid))
            {
                _log.Error($"Import: package {pkg} not installed on this emulator. Install the APK first.");
                return false;
            }

            var ex = await _adb.RootShellAsync(serial,
                $"cd /data/data && tar -xf {AdbService.ShellQuote(remoteTar)} && chown -R {uid}:{uid} {AdbService.ShellQuote($"/data/data/{pkg}")} && restorecon -R {AdbService.ShellQuote($"/data/data/{pkg}")} && echo OK", ct);
            await _adb.RootShellAsync(serial, $"rm -f {AdbService.ShellQuote(remoteTar)}", ct);
            if (!ex.Combined.Contains("OK"))
            {
                _log.Error("Import extract failed: " + ex.Combined.Trim());
                return false;
            }
            _log.Success($"Imported {pkg} (uid {uid}).");
            return true;
        }
        finally
        {
            if (remoteTar is not null)
            {
                try { await _adb.RootShellAsync(serial, $"rm -f {AdbService.ShellQuote(remoteTar)}", CancellationToken.None); }
                catch (Exception ex) { _log.Warning($"Import remote cleanup failed for {remoteTar}: {ex.Message}"); }
            }
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    public static bool TryValidateZipForExtraction(
        string zipPath,
        out string detail,
        int maxEntries = DefaultZipMaxEntries,
        long maxEntryUncompressedBytes = DefaultZipMaxEntryUncompressedBytes,
        long maxTotalUncompressedBytes = DefaultZipMaxTotalUncompressedBytes,
        double maxCompressionRatio = DefaultZipMaxCompressionRatio)
    {
        if (maxEntries <= 0)
        {
            detail = "entry limit must be positive";
            return false;
        }

        if (maxEntryUncompressedBytes <= 0 || maxTotalUncompressedBytes <= 0)
        {
            detail = "size limits must be positive";
            return false;
        }

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.Entries.Count == 0)
            {
                detail = "archive is empty";
                return false;
            }

            if (zip.Entries.Count > maxEntries)
            {
                detail = $"archive has too many entries ({zip.Entries.Count} > {maxEntries})";
                return false;
            }

            long totalUncompressed = 0;
            foreach (var entry in zip.Entries)
            {
                var name = NormalizeZipEntryName(entry.FullName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    detail = "empty entry name";
                    return false;
                }

                if (name.StartsWith("/", StringComparison.Ordinal)
                    || name.Contains('\0')
                    || Path.IsPathRooted(name))
                {
                    detail = $"absolute or invalid path '{entry.FullName}'";
                    return false;
                }

                var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Any(static part => part == ".."))
                {
                    detail = $"path traversal entry '{entry.FullName}'";
                    return false;
                }

                if (entry.Length < 0 || entry.CompressedLength < 0)
                {
                    detail = $"invalid size metadata for '{entry.FullName}'";
                    return false;
                }

                if (entry.Length > maxEntryUncompressedBytes)
                {
                    detail = $"entry '{entry.FullName}' is too large";
                    return false;
                }

                if (entry.Length > 0 && entry.CompressedLength == 0)
                {
                    detail = $"entry '{entry.FullName}' has impossible compression metadata";
                    return false;
                }

                if (entry.Length > 1024 * 1024
                    && entry.CompressedLength > 0
                    && entry.Length / (double)entry.CompressedLength > maxCompressionRatio)
                {
                    detail = $"entry '{entry.FullName}' compression ratio is too high";
                    return false;
                }

                try
                {
                    checked { totalUncompressed += entry.Length; }
                }
                catch (OverflowException)
                {
                    detail = "archive uncompressed size overflows limits";
                    return false;
                }

                if (totalUncompressed > maxTotalUncompressedBytes)
                {
                    detail = "archive uncompressed size exceeds limit";
                    return false;
                }
            }

            detail = "ok";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    public static bool TryValidateDataTarForImport(string tarPath, string pkg, out string detail)
    {
        if (!AdbService.IsSafeAndroidPackageName(pkg))
        {
            detail = "invalid package name";
            return false;
        }

        try
        {
            using var fs = File.OpenRead(tarPath);
            using var reader = new TarReader(fs, leaveOpen: false);
            var sawEntry = false;
            TarEntry? entry;
            while ((entry = reader.GetNextEntry(copyData: false)) is not null)
            {
                sawEntry = true;
                var name = NormalizeTarEntryName(entry.Name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    detail = "empty entry name";
                    return false;
                }
                if (name.StartsWith("/", StringComparison.Ordinal) || name.Contains('\0'))
                {
                    detail = $"absolute or invalid path '{entry.Name}'";
                    return false;
                }

                var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Any(static part => part == ".."))
                {
                    detail = $"path traversal entry '{entry.Name}'";
                    return false;
                }
                if (!name.Equals(pkg, StringComparison.Ordinal)
                    && !name.StartsWith(pkg + "/", StringComparison.Ordinal))
                {
                    detail = $"entry '{entry.Name}' is outside package '{pkg}'";
                    return false;
                }

                if (entry.EntryType is TarEntryType.SymbolicLink
                    or TarEntryType.HardLink
                    or TarEntryType.CharacterDevice
                    or TarEntryType.BlockDevice
                    or TarEntryType.Fifo)
                {
                    detail = $"unsupported unsafe entry type {entry.EntryType} at '{entry.Name}'";
                    return false;
                }
            }

            detail = sawEntry ? "ok" : "tar is empty";
            return sawEntry;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static string NormalizeTarEntryName(string name)
    {
        var normalized = name.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.TrimEnd('/');
    }

    private static string NormalizeZipEntryName(string name)
    {
        var normalized = name.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }
}
