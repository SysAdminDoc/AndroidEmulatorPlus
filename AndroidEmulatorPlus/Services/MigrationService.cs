using System.IO;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

public sealed record TransferResult(string Package, bool Success, long SizeBytes, string Detail);

/// <summary>Port of the bash transfer pipeline: APK install-multiple + tar /data/data + optional ext storage.</summary>
public sealed class MigrationService
{
    private readonly AdbService _adb;
    private readonly LogService _log;

    public MigrationService(AdbService adb, LogService log)
    {
        _adb = adb;
        _log = log;
    }

    public string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "transfer");

    public async Task<TransferResult> TransferApkAsync(string phoneSerial, string emuSerial, string pkg, CancellationToken ct)
    {
        var work = Path.Combine(CacheRoot, pkg);
        Directory.CreateDirectory(work);
        try
        {
            var paths = await _adb.PackagePathsAsync(phoneSerial, pkg, ct);
            if (paths.Count == 0) return new TransferResult(pkg, false, 0, "no path on phone");
            var local = new List<string>();
            foreach (var remote in paths)
            {
                var name = Path.GetFileName(remote);
                var dst = Path.Combine(work, name);
                var pr = await _adb.PullAsync(phoneSerial, remote, dst, ct);
                if (!pr.Success || !File.Exists(dst)) return new TransferResult(pkg, false, 0, "pull failed");
                local.Add(dst);
            }
            var inst = await _adb.InstallAsync(emuSerial, local, ct);
            var total = local.Sum(f => new FileInfo(f).Length);
            if (inst.StdOut.Contains("Success") || inst.Combined.Contains("Success"))
                return new TransferResult(pkg, true, total, $"{local.Count} apk");
            return new TransferResult(pkg, false, total, ParseFailReason(inst.Combined));
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    public async Task<TransferResult> TransferInternalDataAsync(string phoneSerial, string emuSerial, string pkg, CancellationToken ct)
    {
        var tarOnPhone = $"/sdcard/{pkg}.tar";
        var tarOnEmu = $"/sdcard/{pkg}.tar";
        var local = Path.Combine(CacheRoot, $"{pkg}.tar");
        Directory.CreateDirectory(CacheRoot);
        try
        {
            // tar on phone (root) — exclude cache directories
            var tar = await _adb.RootShellAsync(phoneSerial,
                $"cd /data/data && tar --exclude={pkg}/cache --exclude={pkg}/code_cache --exclude={pkg}/no_backup -cf {tarOnPhone} {pkg} && chmod 666 {tarOnPhone}",
                ct);
            if (!tar.Success) return new TransferResult(pkg, false, 0, "tar failed");

            var stat = await _adb.ShellAsync(phoneSerial, $"stat -c %s {tarOnPhone}", ct);
            if (!long.TryParse(stat.StdOut.Trim(), out var size) || size < 1024)
            {
                await _adb.RootShellAsync(phoneSerial, $"rm -f {tarOnPhone}", ct);
                return new TransferResult(pkg, false, 0, "tar empty");
            }

            // pull / push
            var pull = await _adb.PullAsync(phoneSerial, tarOnPhone, local, ct);
            if (!pull.Success || !File.Exists(local)) return new TransferResult(pkg, false, size, "pull failed");
            var push = await _adb.PushAsync(emuSerial, local, tarOnEmu, ct);
            if (!push.Success) return new TransferResult(pkg, false, size, "push failed");

            // force-stop, get UID, untar, chown, restorecon
            await _adb.ShellAsync(emuSerial, $"am force-stop {pkg}", ct);
            var uidR = await _adb.RootShellAsync(emuSerial, $"stat -c %u /data/data/{pkg}", ct);
            if (!int.TryParse(uidR.StdOut.Trim(), out var uid))
                return new TransferResult(pkg, false, size, "no emu uid");

            var ex = await _adb.RootShellAsync(emuSerial,
                $"cd /data/data && tar -xf {tarOnEmu} && chown -R {uid}:{uid} /data/data/{pkg} && restorecon -R /data/data/{pkg} && echo OK",
                ct);
            await _adb.RootShellAsync(emuSerial, $"rm -f {tarOnEmu}", ct);
            await _adb.RootShellAsync(phoneSerial, $"rm -f {tarOnPhone}", ct);

            if (ex.Combined.Contains("OK"))
                return new TransferResult(pkg, true, size, $"uid {uid}");
            return new TransferResult(pkg, false, size, "extract failed: " + ex.Combined.Trim());
        }
        finally
        {
            try { File.Delete(local); } catch { }
        }
    }

    /// <summary>
    /// R-04: tar /storage/emulated/0/Android/obb/&lt;pkg&gt; from the phone to the emulator.
    /// Game OBBs can be huge — opt-in toggle on the Migrate tab.
    /// </summary>
    public async Task<TransferResult> TransferObbAsync(string phoneSerial, string emuSerial, string pkg, CancellationToken ct)
    {
        var tarOnPhone = $"/sdcard/{pkg}_obb.tar";
        var tarOnEmu = $"/sdcard/{pkg}_obb.tar";
        var local = Path.Combine(CacheRoot, $"{pkg}_obb.tar");
        Directory.CreateDirectory(CacheRoot);

        var exists = await _adb.RootShellAsync(phoneSerial,
            $"[ -d /storage/emulated/0/Android/obb/{pkg} ] && echo yes", ct);
        if (!exists.StdOut.Contains("yes")) return new TransferResult(pkg, true, 0, "no obb dir");

        try
        {
            var tar = await _adb.RootShellAsync(phoneSerial,
                $"cd /storage/emulated/0/Android/obb && tar -cf {tarOnPhone} {pkg} && chmod 666 {tarOnPhone}", ct);
            if (!tar.Success) return new TransferResult(pkg, false, 0, "obb tar failed");
            var stat = await _adb.ShellAsync(phoneSerial, $"stat -c %s {tarOnPhone}", ct);
            if (!long.TryParse(stat.StdOut.Trim(), out var size) || size < 1024)
            {
                await _adb.RootShellAsync(phoneSerial, $"rm -f {tarOnPhone}", ct);
                return new TransferResult(pkg, true, 0, "obb empty");
            }
            var pull = await _adb.PullAsync(phoneSerial, tarOnPhone, local, ct);
            if (!pull.Success) return new TransferResult(pkg, false, size, "obb pull failed");
            var push = await _adb.PushAsync(emuSerial, local, tarOnEmu, ct);
            if (!push.Success) return new TransferResult(pkg, false, size, "obb push failed");

            var ex = await _adb.ShellAsync(emuSerial,
                $"mkdir -p /storage/emulated/0/Android/obb/{pkg} && cd /storage/emulated/0/Android/obb && tar -xf {tarOnEmu} && echo OK", ct);
            await _adb.ShellAsync(emuSerial, $"rm -f {tarOnEmu}", ct);
            await _adb.RootShellAsync(phoneSerial, $"rm -f {tarOnPhone}", ct);
            if (ex.Combined.Contains("OK"))
                return new TransferResult(pkg, true, size, "obb ok");
            return new TransferResult(pkg, false, size, "obb extract failed");
        }
        finally
        {
            try { File.Delete(local); } catch { }
        }
    }

    public async Task<TransferResult> TransferExternalDataAsync(string phoneSerial, string emuSerial, string pkg, CancellationToken ct)
    {
        var tarOnPhone = $"/sdcard/{pkg}_ext.tar";
        var tarOnEmu = $"/sdcard/{pkg}_ext.tar";
        var local = Path.Combine(CacheRoot, $"{pkg}_ext.tar");
        Directory.CreateDirectory(CacheRoot);

        var exists = await _adb.RootShellAsync(phoneSerial,
            $"[ -d /storage/emulated/0/Android/data/{pkg} ] && echo yes", ct);
        if (!exists.StdOut.Contains("yes")) return new TransferResult(pkg, true, 0, "no external dir");

        try
        {
            var tar = await _adb.RootShellAsync(phoneSerial,
                $"cd /storage/emulated/0/Android/data && tar -cf {tarOnPhone} {pkg} && chmod 666 {tarOnPhone}", ct);
            if (!tar.Success) return new TransferResult(pkg, false, 0, "ext tar failed");
            var stat = await _adb.ShellAsync(phoneSerial, $"stat -c %s {tarOnPhone}", ct);
            if (!long.TryParse(stat.StdOut.Trim(), out var size) || size < 1024)
            {
                await _adb.RootShellAsync(phoneSerial, $"rm -f {tarOnPhone}", ct);
                return new TransferResult(pkg, true, 0, "ext empty");
            }
            var pull = await _adb.PullAsync(phoneSerial, tarOnPhone, local, ct);
            if (!pull.Success) return new TransferResult(pkg, false, size, "ext pull failed");
            var push = await _adb.PushAsync(emuSerial, local, tarOnEmu, ct);
            if (!push.Success) return new TransferResult(pkg, false, size, "ext push failed");

            var ex = await _adb.RootShellAsync(emuSerial,
                $"mkdir -p /storage/emulated/0/Android/data/{pkg} && cd /storage/emulated/0/Android/data && tar -xf {tarOnEmu} && echo OK",
                ct);
            await _adb.RootShellAsync(emuSerial, $"rm -f {tarOnEmu}", ct);
            await _adb.RootShellAsync(phoneSerial, $"rm -f {tarOnPhone}", ct);
            if (ex.Combined.Contains("OK"))
                return new TransferResult(pkg, true, size, "ext ok");
            return new TransferResult(pkg, false, size, "ext extract failed");
        }
        finally
        {
            try { File.Delete(local); } catch { }
        }
    }

    private static string ParseFailReason(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var idx = line.IndexOf("INSTALL_FAILED_", StringComparison.Ordinal);
            if (idx >= 0)
            {
                var rest = line[idx..];
                var end = rest.IndexOfAny(new[] { ':', ' ', ']' });
                return end > 0 ? rest[..end] : rest;
            }
        }
        return "install failed";
    }
}
