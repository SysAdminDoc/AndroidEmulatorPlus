using System.IO;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// C-19 extraction from AppsViewModel: bundle install orchestration (zip extract,
/// base-first ordering, install-multiple, OBB push, cleanup) and
/// verify-before-install (apksigner verify + installed-cert cross-check via aapt2).
///
/// AppsViewModel keeps the per-row dispatch loop; this service owns the
/// per-file pipeline.
/// </summary>
public sealed class BundleInstallerService
{
    private readonly AppService _apps;
    private readonly AdbService _adb;
    private readonly ApkSignerService _signer;
    private readonly LogService _log;

    public BundleInstallerService(AppService apps, AdbService adb, ApkSignerService signer, LogService log)
    {
        _apps = apps;
        _adb = adb;
        _signer = signer;
        _log = log;
    }

    /// <summary>
    /// Install a single file (.apk / .apks / .xapk / .apkm) onto <paramref name="serial"/>,
    /// optionally running the verify-before-install gate. Returns true on success.
    /// </summary>
    public async Task<bool> InstallFileAsync(string serial, string path, bool verifySignatures, System.Func<string, string?, System.Threading.Tasks.Task<bool>>? onSignerMismatch = null)
    {
        if (verifySignatures && !await VerifyAsync(serial, path, onSignerMismatch))
            return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".apks" or ".xapk" or ".apkm"
            ? await InstallBundleAsync(serial, path)
            : await InstallSingleAsync(serial, path);
    }

    private async Task<bool> InstallSingleAsync(string serial, string apk)
    {
        var r = await _apps.InstallApkAsync(serial, apk);
        if (r.Combined.Contains("Success")) return true;
        _log.Error("install failed: " + r.Combined.Trim());
        return false;
    }

    private async Task<bool> InstallBundleAsync(string serial, string bundlePath)
    {
        AppService.BundleExtractResult? extracted = null;
        try
        {
            try { extracted = _apps.ExtractBundle(bundlePath); }
            catch (System.Exception ex) { _log.Error(ex.Message); return false; }

            var inst = await _apps.InstallSplitApksAsync(serial, extracted.Apks);
            if (!inst.Combined.Contains("Success"))
            {
                _log.Error("bundle install failed: " + inst.Combined.Trim());
                return false;
            }

            if (extracted.ObbFiles.Count > 0)
            {
                if (string.IsNullOrEmpty(extracted.ObbPackage))
                {
                    _log.Warning($"{extracted.ObbFiles.Count} OBB(s) found but the bundle didn't declare a package name. Skipping OBB push.");
                }
                else
                {
                    int okObb = 0;
                    foreach (var obb in extracted.ObbFiles)
                        if (await _apps.PushObbAsync(serial, extracted.ObbPackage, obb)) okObb++;
                    _log.Info($"OBB push: {okObb}/{extracted.ObbFiles.Count} ok.");
                }
            }
            return true;
        }
        finally
        {
            if (extracted is not null)
            {
                try { Directory.Delete(extracted.WorkDir, true); } catch { }
            }
        }
    }

    /// <summary>
    /// R-08 / C-04: verify APK signatures before install. Bundles are inspected
    /// split-by-split so a bad config APK cannot hide behind a valid base APK.
    /// Returns true if install should proceed. <paramref name="onSignerMismatch"/>
    /// is invoked when the installed package has a different cert; return true to
    /// proceed anyway.
    /// </summary>
    private async Task<bool> VerifyAsync(string serial, string path, System.Func<string, string?, System.Threading.Tasks.Task<bool>>? onSignerMismatch)
    {
        if (!_signer.IsAvailable)
        {
            _log.Warning("Sig verify skipped: apksigner.bat not found in SDK build-tools.");
            return true;
        }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        string? tempDir = null;
        try
        {
            var apkPaths = new List<string> { path };
            if (ext is ".apks" or ".xapk" or ".apkm")
            {
                tempDir = Path.Combine(Path.GetTempPath(), $"aep-verify-{System.Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                System.IO.Compression.ZipFile.ExtractToDirectory(path, tempDir);
                apkPaths = AppService.OrderBaseFirst(
                    Directory.EnumerateFiles(tempDir, "*.apk", SearchOption.AllDirectories));
                if (apkPaths.Count == 0)
                {
                    _log.Error($"Signature verify failed for {Path.GetFileName(path)}: no APKs found inside bundle.");
                    return false;
                }
            }

            string? expectedSha = null;
            string? baseSha = null;
            foreach (var apk in apkPaths)
            {
                var info = await _signer.InspectAsync(apk);
                if (!info.Verified)
                {
                    _log.Error($"apksigner verify FAILED for {Path.GetFileName(apk)}: {info.Raw.Trim()}");
                    return false;
                }

                if (info.Sha256 is not null)
                {
                    expectedSha ??= info.Sha256;
                    if (string.Equals(apk, apkPaths[0], System.StringComparison.OrdinalIgnoreCase))
                        baseSha = info.Sha256;
                    if (!string.Equals(expectedSha, info.Sha256, System.StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Error($"Signature verify failed for {Path.GetFileName(path)}: split {Path.GetFileName(apk)} uses a different signing certificate.");
                        return false;
                    }
                }
            }

            baseSha ??= expectedSha;
            if (baseSha is null) return true;

            _log.Detail($"signer cert SHA-256 {baseSha[..12]}…{(apkPaths.Count > 1 ? $" ({apkPaths.Count} split APKs verified)" : "")}");
            var pkg = await _signer.ReadPackageNameAsync(apkPaths[0]);
            if (pkg is null) return true;
            var installedSha = await _signer.InstalledCertShaAsync(_adb, serial, pkg);
            if (installedSha is null) return true;

            if (string.Equals(baseSha, installedSha, System.StringComparison.OrdinalIgnoreCase))
            {
                _log.Detail($"signer matches installed cert for {pkg}");
                return true;
            }
            // Mismatch — defer to caller so the UI thread can raise a dialog.
            if (onSignerMismatch is null)
            {
                _log.Warning($"Signer mismatch for {pkg} — no UI prompt registered; allowing install.");
                return true;
            }
            return await onSignerMismatch(pkg, $"new={baseSha[..12]}…  installed={installedSha[..12]}…");
        }
        catch (System.Exception ex)
        {
            _log.Error("Signature verify failed: " + ex.Message);
            return false;
        }
        finally
        {
            if (tempDir is not null) try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
