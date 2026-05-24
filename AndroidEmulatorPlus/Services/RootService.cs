using System.Diagnostics;
using System.IO;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

/// <summary>Root via rootAVD: clone the repo, swap in latest Magisk, patch ramdisk.img, install Magisk APK.</summary>
public sealed class RootService
{
    private readonly SdkLocator _sdk;
    private readonly AdbService _adb;
    private readonly DownloadService _dl;
    private readonly LogService _log;
    private readonly EmulatorService _emu;

    public RootService(SdkLocator sdk, AdbService adb, DownloadService dl, LogService log, EmulatorService emu)
    {
        _sdk = sdk;
        _adb = adb;
        _dl = dl;
        _log = log;
        _emu = emu;
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
        status?.Report("Cloning rootAVD (GitLab)…");
        var r = await ProcessRunner.RunAsync("git",
            new[] { "clone", "--depth", "1", "https://gitlab.com/newbit/rootAVD.git", RootAvdDir },
            ct: ct);
        if (!r.Success) throw new InvalidOperationException("git clone of rootAVD failed: " + r.Combined);
    }

    public async Task<string> DownloadLatestMagiskAsync(IProgress<string>? status, CancellationToken ct)
    {
        status?.Report("Looking up latest Magisk release…");
        var info = await _dl.LatestMagiskAsync(ct) ?? throw new InvalidOperationException(
            "Could not resolve latest Magisk from GitHub releases. Check network connectivity.");
        var (tag, url) = info;
        status?.Report($"Downloading Magisk {tag}…");
        await _dl.DownloadAsync(url, MagiskApkPath, null, ct);
        // rootAVD treats the same artifact as a zip when patching:
        File.Copy(MagiskApkPath, Path.Combine(RootAvdDir, "Magisk.zip"), overwrite: true);
        var appsDir = Path.Combine(RootAvdDir, "Apps");
        Directory.CreateDirectory(appsDir);
        try { File.Delete(Path.Combine(appsDir, "Magisk.apk")); } catch { }
        return tag;
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

    public async Task<bool> PatchAsync(string ramdiskRelative, IProgress<string>? status,
        Action<string>? onOutput, CancellationToken ct)
    {
        if (_sdk.SdkRoot is null) throw new InvalidOperationException("SDK root not located.");
        status?.Report("Running rootAVD.sh…");
        var env = new Dictionary<string, string?>
        {
            ["ANDROID_HOME"] = _sdk.SdkRoot,
            ["ANDROID_SDK_ROOT"] = _sdk.SdkRoot,
            ["MSYS_NO_PATHCONV"] = "1",
            ["MSYS2_ARG_CONV_EXCL"] = "*",
            ["ANDROID_SERIAL"] = "emulator-5554",
        };
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "");
        env["PATH"] = Path.GetDirectoryName(_sdk.AdbExe!) + ";" + path;

        var bash = TryFindBash();
        if (bash is null) throw new InvalidOperationException(
            "bash.exe not found. rootAVD needs Git Bash or WSL.\nInstall Git for Windows and retry.");

        var script = Path.Combine(RootAvdDir, "rootAVD.sh");
        var psi = new ProcessStartInfo
        {
            FileName = bash,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RootAvdDir,
        };
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(ramdiskRelative);
        foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput?.Invoke(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onOutput?.Invoke(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);
        return proc.ExitCode == 0;
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
