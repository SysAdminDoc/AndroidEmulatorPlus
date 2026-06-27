using System.IO;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

/// <summary>Root via rootAVD: clone the repo, swap in latest Magisk, patch ramdisk.img, install Magisk APK.</summary>
public sealed class RootService
{
    // Pin rootAVD to a verified revision. master can introduce breaking changes
    // that would silently brick this app's root flow. Bump after manual smoke-test.
    //
    // 2026-05-25 — initial pin lands at gitlab.com/newbit/rootAVD HEAD as of v0.2.0.
    // Source: `git ls-remote https://gitlab.com/newbit/rootAVD.git HEAD`. The
    // ListAllAVDs entry-point (used by RootService.DryRunAsync) is present at
    // line 2733 of rootAVD.sh on this revision — verified.
    public const string RootAvdPinnedRef = "613caa44371f85e1a461bc030e07ddc2d71afe32";

    private static readonly TimeSpan PatchTimeout = TimeSpan.FromMinutes(10);

    private readonly SdkLocator _sdk;
    private readonly AdbService _adb;
    private readonly DownloadService _dl;
    private readonly LogService _log;
    private readonly EmulatorService _emu;
    private readonly HashVerificationService _hash;

    public RootService(SdkLocator sdk, AdbService adb, DownloadService dl, LogService log, EmulatorService emu, HashVerificationService hash)
    {
        _sdk = sdk;
        _adb = adb;
        _dl = dl;
        _log = log;
        _emu = emu;
        _hash = hash;
    }

    public string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "cache");
    public string RootAvdDir => Path.Combine(CacheRoot, "rootAVD");
    public string MagiskApkPath => Path.Combine(CacheRoot, "Magisk.apk");

    public async Task EnsureRootAvdAsync(IProgress<string>? status, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheRoot);
        var script = Path.Combine(RootAvdDir, "rootAVD.sh");
        if (File.Exists(script))
        {
            status?.Report("rootAVD already cached");
            return;
        }
        var isPinned = !RootAvdPinnedRef.Equals("master", StringComparison.Ordinal);
        status?.Report(isPinned ? $"Cloning rootAVD (pin {RootAvdPinnedRef[..Math.Min(7, RootAvdPinnedRef.Length)]})…" : "Cloning rootAVD (master)…");

        // Pinned refs need full history so the SHA is resolvable; otherwise use a shallow clone.
        var cloneArgs = isPinned
            ? new[] { "clone", "https://gitlab.com/newbit/rootAVD.git", RootAvdDir }
            : new[] { "clone", "--depth", "1", "https://gitlab.com/newbit/rootAVD.git", RootAvdDir };
        var r = await ProcessRunner.RunAsync("git", cloneArgs, ct: ct);
        if (!r.Success) throw new InvalidOperationException("git clone of rootAVD failed: " + r.Combined);

        if (isPinned)
        {
            status?.Report($"Checking out {RootAvdPinnedRef}…");
            var co = await ProcessRunner.RunAsync("git",
                new[] { "checkout", "--detach", RootAvdPinnedRef },
                workingDir: RootAvdDir, ct: ct);
            if (!co.Success) throw new InvalidOperationException("git checkout of pinned rootAVD revision failed: " + co.Combined);
        }
    }

    public async Task<string> DownloadLatestMagiskAsync(IProgress<string>? status, CancellationToken ct,
        IProgress<(long received, long? total)>? downloadProgress = null)
    {
        status?.Report("Looking up latest Magisk release…");
        var info = await _dl.LatestMagiskAsync(ct) ?? throw new InvalidOperationException(
            "Could not resolve latest Magisk from GitHub releases. Check network connectivity.");
        status?.Report($"Downloading Magisk {info.Tag}…");
        await _dl.DownloadAsync(info.Url, MagiskApkPath, downloadProgress, ct);

        var assetName = System.IO.Path.GetFileName(new Uri(info.Url).LocalPath);
        var actualSha = HashVerificationService.ComputeSha256(MagiskApkPath);

        // Defense-in-depth tier 1: GitHub publishes a per-asset SHA-256 digest in the
        // Releases API. When present, the downloaded file MUST match it — this catches
        // a corrupted download or a mirror compromise that the in-tree manifest can't.
        if (info.GitHubDigestSha256 is { } pub
            && !string.Equals(actualSha, pub, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(MagiskApkPath); } catch { }
            throw new InvalidOperationException(
                $"Magisk APK SHA-256 mismatch vs GitHub-published digest. Expected {pub}, got {actualSha}. Partial download deleted.");
        }

        // Defense-in-depth tier 2: in-tree manifest (curated, maintainer-signed-off).
        var check = _hash.VerifyMagisk(assetName, MagiskApkPath);
        if (!check.Ok)
        {
            try { File.Delete(MagiskApkPath); } catch { }
            throw new InvalidOperationException(
                $"Magisk APK SHA-256 verification failed: {check.Detail}. Partial download deleted.");
        }

        // rootAVD treats the same artifact as a zip when patching:
        File.Copy(MagiskApkPath, Path.Combine(RootAvdDir, "Magisk.zip"), overwrite: true);
        var appsDir = Path.Combine(RootAvdDir, "Apps");
        Directory.CreateDirectory(appsDir);
        try { File.Delete(Path.Combine(appsDir, "Magisk.apk")); } catch { }
        return info.Tag;
    }

    public string? FindRamdiskFor(string avdName)
    {
        if (_sdk.SdkRoot is null) return null;
        var avdDir = Path.Combine(_sdk.AvdHome!, avdName + ".avd");
        var cfg = Path.Combine(avdDir, "config.ini");
        if (!File.Exists(cfg)) return null;
        var ini = Services.AvdService.ParseIni(cfg);
        var sysdir = ini.GetValueOrDefault("image.sysdir.1");
        if (sysdir is null) return null;
        var full = Path.Combine(_sdk.SdkRoot, sysdir.Replace('\\', Path.DirectorySeparatorChar), "ramdisk.img");
        return File.Exists(full) ? full : null;
    }

    public string? RelativeRamdiskPath(string ramdiskFullPath)
    {
        if (_sdk.SdkRoot is null) return null;
        var rel = Path.GetRelativePath(_sdk.SdkRoot, ramdiskFullPath);
        return rel.Replace('\\', '/');
    }

    private Dictionary<string, string?> RootAvdEnv()
    {
        var env = new Dictionary<string, string?>
        {
            ["ANDROID_HOME"] = _sdk.SdkRoot,
            ["ANDROID_SDK_ROOT"] = _sdk.SdkRoot,
            ["MSYS_NO_PATHCONV"] = "1",
            ["MSYS2_ARG_CONV_EXCL"] = "*",
            ["ANDROID_SERIAL"] = "emulator-5554",
        };
        env["PATH"] = Path.GetDirectoryName(_sdk.AdbExe!) + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");
        return env;
    }

    /// <summary>
    /// B-08: rootAVD's `LISTONLY=1` mode prints what it would patch without changing
    /// anything. The script enumerates AVDs and their ramdisk paths. Output goes to
    /// <paramref name="onOutput"/> for the UI log.
    /// </summary>
    public async Task<bool> DryRunAsync(IProgress<string>? status, Action<string>? onOutput, CancellationToken ct)
    {
        if (_sdk.SdkRoot is null) throw new InvalidOperationException("SDK root not located.");
        status?.Report("Running rootAVD.sh LISTONLY…");
        var bash = TryFindBash() ?? throw new InvalidOperationException(
            "bash.exe not found. Install Git for Windows.");
        try
        {
            var exit = await ProcessRunner.StreamAsync(bash,
                new[] { Path.Combine(RootAvdDir, "rootAVD.sh"), "ListAllAVDs" },
                onLine: l => onOutput?.Invoke(l),
                workingDir: RootAvdDir,
                extraEnv: RootAvdEnv(),
                timeout: TimeSpan.FromMinutes(2),
                ct: ct);
            return exit == 0;
        }
        catch (TimeoutException)
        {
            _log.Error("rootAVD.sh LISTONLY exceeded 2 min timeout — killed.");
            return false;
        }
        catch (OperationCanceledException) { return false; }
    }

    public async Task<bool> PatchAsync(string ramdiskRelative, IProgress<string>? status,
        Action<string>? onOutput, CancellationToken ct)
    {
        if (_sdk.SdkRoot is null) throw new InvalidOperationException("SDK root not located.");
        status?.Report("Running rootAVD.sh…");
        var bash = TryFindBash() ?? throw new InvalidOperationException(
            "bash.exe not found. rootAVD needs Git Bash or WSL.\nInstall Git for Windows and retry.");

        // rootAVD.sh has been observed to hang when the target emulator deadlocks
        // during the ramdisk swap. Cap the run so the UI doesn't get stuck forever.
        try
        {
            var exit = await ProcessRunner.StreamAsync(bash,
                new[] { Path.Combine(RootAvdDir, "rootAVD.sh"), ramdiskRelative },
                onLine: l => onOutput?.Invoke(l),
                workingDir: RootAvdDir,
                extraEnv: RootAvdEnv(),
                timeout: PatchTimeout,
                ct: ct);
            return exit == 0;
        }
        catch (TimeoutException)
        {
            _log.Error($"rootAVD.sh exceeded {PatchTimeout.TotalMinutes:0} min timeout — killed.");
            return false;
        }
    }

    public async Task<bool> VerifyRootAsync(string serial, CancellationToken ct)
    {
        // Multiple tries because the dialog cache can lag.
        for (int i = 0; i < 3; i++)
        {
            var ok = await _adb.IsRootedAsync(serial, ct);
            if (ok) return true;
            await Task.Delay(2000, ct);
        }
        return false;
    }

    /// <summary>Tells Magisk to always allow shell (uid 2000), so future adb calls don't trigger the dialog.</summary>
    public async Task PersistShellPolicyAsync(string serial, CancellationToken ct)
    {
        var r = await _adb.RootShellAsync(serial,
            "magisk --sqlite \"REPLACE INTO policies (uid, policy, until, logging, notification) VALUES (2000, 2, 0, 1, 1)\"",
            ct);
        if (!r.Success) _log.Warning("Persisting shell policy: " + r.Combined.Trim());
        else _log.Info("Magisk policy: shell→allow persisted");
    }

    public string? FindBackupRamdisk(string ramdiskFullPath)
    {
        foreach (var ext in new[] { ".original", ".backup" })
        {
            var p = ramdiskFullPath + ext;
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public void RestoreRamdisk(string ramdiskFullPath)
    {
        var backup = FindBackupRamdisk(ramdiskFullPath)
            ?? throw new InvalidOperationException("No backup ramdisk found.");
        File.Copy(backup, ramdiskFullPath, overwrite: true);
        _log.Info($"Restored stock ramdisk from {Path.GetFileName(backup)}.");
    }

    private static string? TryFindBash()
    {
        var candidates = new[]
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
            @"C:\Program Files\Git\usr\bin\bash.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\bin\bash.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
