using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

public sealed record TransferResult(string Package, bool Success, long SizeBytes, string Detail);

public sealed class MigrationLegReceipt
{
    [JsonPropertyName("leg")] public string Leg { get; init; } = "";
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
    [JsonPropertyName("detail")] public string Detail { get; init; } = "";
}

public sealed class MigrationPackageReceipt
{
    [JsonPropertyName("package")] public string Package { get; init; } = "";
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("legs")] public List<MigrationLegReceipt> Legs { get; init; } = [];
}

public sealed class MigrationReceipt
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; init; } = "";
    [JsonPropertyName("sourceSerial")] public string SourceSerial { get; init; } = "";
    [JsonPropertyName("targetSerial")] public string TargetSerial { get; init; } = "";
    [JsonPropertyName("scopes")] public List<string> Scopes { get; init; } = [];
    [JsonPropertyName("packages")] public List<MigrationPackageReceipt> Packages { get; init; } = [];
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    [JsonPropertyName("successCount")] public int SuccessCount { get; init; }
    [JsonPropertyName("failCount")] public int FailCount { get; init; }
    [JsonPropertyName("cancelled")] public bool Cancelled { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string> FailedPackages =>
        Packages.Where(p => !p.Success).Select(p => p.Package).ToList();
}

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

    /// <summary>If true, also force-stop the package on the source phone before tarring (A-30).</summary>
    public bool ForceStopOnPhone { get; set; }

    /// <summary>
    /// C-05: Probe `pm dump &lt;pkg&gt;` for the ALLOW_BACKUP flag. Apps that declare
    /// <c>android:allowBackup="false"</c> in AndroidManifest will refuse the
    /// restored data after migration; the Migrate flow defaults to skipping the
    /// internal-data leg for them.
    /// </summary>
    public async Task<bool> AllowsBackupAsync(string serial, string pkg, CancellationToken ct = default)
    {
        if (!AdbService.IsSafeAndroidPackageName(pkg)) return true;
        try
        {
            var r = await _adb.ShellAsync(serial, $"pm dump {Q(pkg)}", ct);
            if (!r.Success) return true; // assume yes when we can't tell
            // pm dump output lines that name the flag look like:
            //   flags=[ ALLOW_BACKUP ALLOW_CLEAR_USER_DATA HAS_CODE … ]
            // When backup is disabled, ALLOW_BACKUP is simply omitted from the
            // flags list. Look for the token to decide.
            var hay = r.StdOut;
            // The "flags" line is the most reliable; restrict the search to lines
            // mentioning "flags=" to avoid matching unrelated docstrings.
            foreach (var line in hay.Split('\n'))
            {
                if (line.Contains("flags=", StringComparison.OrdinalIgnoreCase)
                    && line.Contains('[', StringComparison.Ordinal))
                {
                    return line.Contains("ALLOW_BACKUP", StringComparison.Ordinal);
                }
            }
            // Newer AOSP printouts use 'flag.ALLOW_BACKUP=true|false' style; fall back.
            var m = System.Text.RegularExpressions.Regex.Match(hay,
                @"allowBackup\s*=\s*(true|false)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            return true;
        }
        catch
        {
            return true; // tolerate adb hiccups
        }
    }

    // A-29: cache the phone's tar flavor per serial so we don't re-detect on every package.
    // The boolean tracks whether `tar --exclude=` is supported (toybox 0.7+, GNU tar).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _phoneTarSupportsExclude = new(StringComparer.Ordinal);

    private async Task<bool> PhoneTarSupportsExcludeAsync(string serial, CancellationToken ct)
    {
        if (_phoneTarSupportsExclude.TryGetValue(serial, out var v)) return v;
        var probe = await _adb.ShellAsync(serial, "tar --help 2>&1 | grep -i exclude || echo NO", ct);
        var supports = probe.StdOut.Contains("exclude", StringComparison.OrdinalIgnoreCase)
                    && !probe.StdOut.TrimEnd().EndsWith("NO", StringComparison.Ordinal);
        _phoneTarSupportsExclude[serial] = supports;
        _log.Detail($"phone tar (serial {serial}) supports --exclude=: {supports}");
        return supports;
    }

    public string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "transfer");

    public async Task<TransferResult> TransferApkAsync(string phoneSerial, string emuSerial, string pkg, CancellationToken ct)
    {
        if (!AdbService.IsSafeAndroidPackageName(pkg))
            return new TransferResult(pkg, false, 0, "invalid package name");
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
        if (!AdbService.IsSafeAndroidPackageName(pkg))
            return new TransferResult(pkg, false, 0, "invalid package name");
        var tarOnPhone = $"/sdcard/{pkg}.tar";
        var tarOnEmu = $"/sdcard/{pkg}.tar";
        var local = Path.Combine(CacheRoot, $"{pkg}.tar");
        Directory.CreateDirectory(CacheRoot);
        try
        {
            // A-30: optionally force-stop on the phone before tarring so the DB isn't torn.
            if (ForceStopOnPhone)
            {
                try { await _adb.ShellAsync(phoneSerial, $"am force-stop {Q(pkg)}", ct); } catch { }
            }

            // A-29: pick the right tar pipeline for the phone's tar flavor.
            string tarCmd;
            if (await PhoneTarSupportsExcludeAsync(phoneSerial, ct))
            {
                tarCmd = $"cd /data/data && tar --exclude={Q(pkg + "/cache")} --exclude={Q(pkg + "/code_cache")} --exclude={Q(pkg + "/no_backup")} -cf {Q(tarOnPhone)} {Q(pkg)} && chmod 666 {Q(tarOnPhone)}";
            }
            else
            {
                // Older toybox tar lacks --exclude=; use find with -prune to skip cache dirs
                // and feed the surviving paths via -T -.
                tarCmd = $"cd /data/data && find {Q(pkg)} -type d \\( -name cache -o -name code_cache -o -name no_backup \\) -prune -o -print | tar -cf {Q(tarOnPhone)} -T - && chmod 666 {Q(tarOnPhone)}";
            }
            var tar = await _adb.RootShellAsync(phoneSerial, tarCmd, ct);
            if (!tar.Success) return new TransferResult(pkg, false, 0, "tar failed");

            var stat = await _adb.ShellAsync(phoneSerial, $"stat -c %s {Q(tarOnPhone)}", ct);
            if (!long.TryParse(stat.StdOut.Trim(), out var size) || size < 1024)
            {
                await _adb.RootShellAsync(phoneSerial, $"rm -f {Q(tarOnPhone)}", ct);
                return new TransferResult(pkg, false, 0, "tar empty");
            }

            // pull / push
            var pull = await _adb.PullAsync(phoneSerial, tarOnPhone, local, ct);
            if (!pull.Success || !File.Exists(local)) return new TransferResult(pkg, false, size, "pull failed");
            if (!AppService.TryValidateDataTarForImport(local, pkg, out var tarDetail))
                return new TransferResult(pkg, false, size, "unsafe tar: " + tarDetail);
            var push = await _adb.PushAsync(emuSerial, local, tarOnEmu, ct);
            if (!push.Success) return new TransferResult(pkg, false, size, "push failed");

            // force-stop, get UID, untar, chown, restorecon
            await _adb.ShellAsync(emuSerial, $"am force-stop {Q(pkg)}", ct);
            var uidR = await _adb.RootShellAsync(emuSerial, $"stat -c %u {Q($"/data/data/{pkg}")}", ct);
            if (!int.TryParse(uidR.StdOut.Trim(), out var uid))
                return new TransferResult(pkg, false, size, "no emu uid");

            var ex = await _adb.RootShellAsync(emuSerial,
                $"cd /data/data && tar -xf {Q(tarOnEmu)} && chown -R {uid}:{uid} {Q($"/data/data/{pkg}")} && restorecon -R {Q($"/data/data/{pkg}")} && echo OK",
                ct);
            await _adb.RootShellAsync(emuSerial, $"rm -f {Q(tarOnEmu)}", ct);
            await _adb.RootShellAsync(phoneSerial, $"rm -f {Q(tarOnPhone)}", ct);

            if (ex.Combined.Contains("OK"))
                return new TransferResult(pkg, true, size, $"uid {uid}");
            return new TransferResult(pkg, false, size, "extract failed: " + ex.Combined.Trim());
        }
        finally
        {
            await RemoveRemoteFileBestEffortAsync(phoneSerial, tarOnPhone, root: true);
            await RemoveRemoteFileBestEffortAsync(emuSerial, tarOnEmu, root: true);
            try { File.Delete(local); } catch { }
        }
    }

    /// <summary>
    /// R-04: tar /storage/emulated/0/Android/obb/&lt;pkg&gt; from the phone to the emulator.
    /// Game OBBs can be huge — opt-in toggle on the Migrate tab.
    /// </summary>
    public async Task<TransferResult> TransferObbAsync(string phoneSerial, string emuSerial, string pkg, CancellationToken ct)
    {
        if (!AdbService.IsSafeAndroidPackageName(pkg))
            return new TransferResult(pkg, false, 0, "invalid package name");
        var tarOnPhone = $"/sdcard/{pkg}_obb.tar";
        var tarOnEmu = $"/sdcard/{pkg}_obb.tar";
        var local = Path.Combine(CacheRoot, $"{pkg}_obb.tar");
        Directory.CreateDirectory(CacheRoot);

        var exists = await _adb.RootShellAsync(phoneSerial,
            $"[ -d {Q($"/storage/emulated/0/Android/obb/{pkg}")} ] && echo yes", ct);
        if (!exists.StdOut.Contains("yes")) return new TransferResult(pkg, true, 0, "no obb dir");

        try
        {
            var tar = await _adb.RootShellAsync(phoneSerial,
                $"cd /storage/emulated/0/Android/obb && tar -cf {Q(tarOnPhone)} {Q(pkg)} && chmod 666 {Q(tarOnPhone)}", ct);
            if (!tar.Success) return new TransferResult(pkg, false, 0, "obb tar failed");
            var stat = await _adb.ShellAsync(phoneSerial, $"stat -c %s {Q(tarOnPhone)}", ct);
            if (!long.TryParse(stat.StdOut.Trim(), out var size) || size < 1024)
            {
                await _adb.RootShellAsync(phoneSerial, $"rm -f {Q(tarOnPhone)}", ct);
                return new TransferResult(pkg, true, 0, "obb empty");
            }
            var pull = await _adb.PullAsync(phoneSerial, tarOnPhone, local, ct);
            if (!pull.Success) return new TransferResult(pkg, false, size, "obb pull failed");
            if (!AppService.TryValidateDataTarForImport(local, pkg, out var tarDetail))
                return new TransferResult(pkg, false, size, "unsafe obb tar: " + tarDetail);
            var push = await _adb.PushAsync(emuSerial, local, tarOnEmu, ct);
            if (!push.Success) return new TransferResult(pkg, false, size, "obb push failed");

            var ex = await _adb.ShellAsync(emuSerial,
                $"mkdir -p {Q($"/storage/emulated/0/Android/obb/{pkg}")} && cd /storage/emulated/0/Android/obb && tar -xf {Q(tarOnEmu)} && echo OK", ct);
            await _adb.ShellAsync(emuSerial, $"rm -f {Q(tarOnEmu)}", ct);
            await _adb.RootShellAsync(phoneSerial, $"rm -f {Q(tarOnPhone)}", ct);
            if (ex.Combined.Contains("OK"))
                return new TransferResult(pkg, true, size, "obb ok");
            return new TransferResult(pkg, false, size, "obb extract failed");
        }
        finally
        {
            await RemoveRemoteFileBestEffortAsync(phoneSerial, tarOnPhone, root: true);
            await RemoveRemoteFileBestEffortAsync(emuSerial, tarOnEmu, root: false);
            try { File.Delete(local); } catch { }
        }
    }

    public async Task<TransferResult> TransferExternalDataAsync(string phoneSerial, string emuSerial, string pkg, CancellationToken ct)
    {
        if (!AdbService.IsSafeAndroidPackageName(pkg))
            return new TransferResult(pkg, false, 0, "invalid package name");
        var tarOnPhone = $"/sdcard/{pkg}_ext.tar";
        var tarOnEmu = $"/sdcard/{pkg}_ext.tar";
        var local = Path.Combine(CacheRoot, $"{pkg}_ext.tar");
        Directory.CreateDirectory(CacheRoot);

        var exists = await _adb.RootShellAsync(phoneSerial,
            $"[ -d {Q($"/storage/emulated/0/Android/data/{pkg}")} ] && echo yes", ct);
        if (!exists.StdOut.Contains("yes")) return new TransferResult(pkg, true, 0, "no external dir");

        try
        {
            var tar = await _adb.RootShellAsync(phoneSerial,
                $"cd /storage/emulated/0/Android/data && tar -cf {Q(tarOnPhone)} {Q(pkg)} && chmod 666 {Q(tarOnPhone)}", ct);
            if (!tar.Success) return new TransferResult(pkg, false, 0, "ext tar failed");
            var stat = await _adb.ShellAsync(phoneSerial, $"stat -c %s {Q(tarOnPhone)}", ct);
            if (!long.TryParse(stat.StdOut.Trim(), out var size) || size < 1024)
            {
                await _adb.RootShellAsync(phoneSerial, $"rm -f {Q(tarOnPhone)}", ct);
                return new TransferResult(pkg, true, 0, "ext empty");
            }
            var pull = await _adb.PullAsync(phoneSerial, tarOnPhone, local, ct);
            if (!pull.Success) return new TransferResult(pkg, false, size, "ext pull failed");
            if (!AppService.TryValidateDataTarForImport(local, pkg, out var tarDetail))
                return new TransferResult(pkg, false, size, "unsafe ext tar: " + tarDetail);
            var push = await _adb.PushAsync(emuSerial, local, tarOnEmu, ct);
            if (!push.Success) return new TransferResult(pkg, false, size, "ext push failed");

            var ex = await _adb.RootShellAsync(emuSerial,
                $"mkdir -p {Q($"/storage/emulated/0/Android/data/{pkg}")} && cd /storage/emulated/0/Android/data && tar -xf {Q(tarOnEmu)} && echo OK",
                ct);
            await _adb.RootShellAsync(emuSerial, $"rm -f {Q(tarOnEmu)}", ct);
            await _adb.RootShellAsync(phoneSerial, $"rm -f {Q(tarOnPhone)}", ct);
            if (ex.Combined.Contains("OK"))
                return new TransferResult(pkg, true, size, "ext ok");
            return new TransferResult(pkg, false, size, "ext extract failed");
        }
        finally
        {
            await RemoveRemoteFileBestEffortAsync(phoneSerial, tarOnPhone, root: true);
            await RemoveRemoteFileBestEffortAsync(emuSerial, tarOnEmu, root: true);
            try { File.Delete(local); } catch { }
        }
    }

    private async Task RemoveRemoteFileBestEffortAsync(string serial, string remotePath, bool root)
    {
        try
        {
            var cmd = $"rm -f {Q(remotePath)}";
            if (root) await _adb.RootShellAsync(serial, cmd, CancellationToken.None);
            else await _adb.ShellAsync(serial, cmd, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Warning($"Remote cleanup failed for {serial}:{remotePath}: {ex.Message}");
        }
    }

    public static string ReceiptDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "logs");

    public string WriteReceipt(MigrationReceipt receipt)
    {
        Directory.CreateDirectory(ReceiptDirectory);
        var name = $"migration-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(ReceiptDirectory, name);
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        _log.Info($"Migration receipt: {path}");
        return path;
    }

    public static MigrationReceipt? ReadLatestReceipt()
    {
        if (!Directory.Exists(ReceiptDirectory)) return null;
        var latest = Directory.EnumerateFiles(ReceiptDirectory, "migration-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        if (latest is null) return null;
        try
        {
            var json = File.ReadAllText(latest);
            return JsonSerializer.Deserialize<MigrationReceipt>(json);
        }
        catch { return null; }
    }

    public sealed record DryRunResult(
        int TotalPackages,
        int NoBackupCount,
        long EstimatedSizeBytes,
        bool PhoneRooted,
        bool EmulatorRooted,
        IReadOnlyList<string> Scopes,
        IReadOnlyList<string> Blockers,
        string Summary)
    {
        public bool CanProceed => Blockers.Count == 0;
    }

    public async Task<DryRunResult> DryRunAsync(
        string phoneSerial,
        string emuSerial,
        IReadOnlyList<string> packages,
        bool doApk,
        bool doInternal,
        bool doExternal,
        bool doObb,
        bool forceDataForNoBackup,
        CancellationToken ct)
    {
        var blockers = new List<string>();
        var scopes = new List<string>();
        if (doApk) scopes.Add("apk");
        if (doInternal) scopes.Add("internal");
        if (doExternal) scopes.Add("external");
        if (doObb) scopes.Add("obb");

        var phoneRooted = await _adb.IsRootedAsync(phoneSerial, ct);
        var emuRooted = await _adb.IsRootedAsync(emuSerial, ct);

        if (doInternal && !phoneRooted)
            blockers.Add("Phone is not rooted — internal data copy requires root on the source.");
        if (doInternal && !emuRooted)
            blockers.Add("Emulator is not rooted — internal data restore requires root on the target.");

        int noBackupCount = 0;
        long estimatedSize = 0;

        foreach (var pkg in packages)
        {
            ct.ThrowIfCancellationRequested();
            if (doApk)
            {
                var paths = await _adb.PackagePathsAsync(phoneSerial, pkg, ct);
                foreach (var path in paths)
                {
                    var stat = await _adb.ShellAsync(phoneSerial, $"stat -c %s {Q(path)}", ct);
                    if (long.TryParse(stat.StdOut.Trim(), out var apkSize))
                        estimatedSize += apkSize;
                }
            }

            if (doInternal && phoneRooted)
            {
                var dataSize = await _adb.DataSizeAsync(phoneSerial, pkg, ct);
                estimatedSize += dataSize;
            }

            if (!await AllowsBackupAsync(phoneSerial, pkg, ct))
                noBackupCount++;
        }

        if (doInternal && noBackupCount > 0 && !forceDataForNoBackup)
            _log.Detail($"{noBackupCount} package(s) have allowBackup=false; their internal data will be skipped.");

        var emuFree = await GetDeviceFreeSpaceAsync(emuSerial, ct);
        if (emuFree.HasValue && estimatedSize > emuFree.Value)
            blockers.Add($"Estimated transfer ({estimatedSize / 1024 / 1024} MB) exceeds emulator free space ({emuFree.Value / 1024 / 1024} MB).");

        var summary = $"{packages.Count} package(s), scopes: [{string.Join(", ", scopes)}], " +
                       $"est. {estimatedSize / 1024 / 1024} MB, " +
                       $"phone root: {(phoneRooted ? "yes" : "no")}, " +
                       $"emu root: {(emuRooted ? "yes" : "no")}, " +
                       $"{noBackupCount} no-backup" +
                       (blockers.Count > 0 ? $", BLOCKED: {string.Join("; ", blockers)}" : "");

        return new DryRunResult(packages.Count, noBackupCount, estimatedSize,
            phoneRooted, emuRooted, scopes, blockers, summary);
    }

    private async Task<long?> GetDeviceFreeSpaceAsync(string serial, CancellationToken ct)
    {
        try
        {
            var r = await _adb.ShellAsync(serial, "df /data | tail -1 | awk '{print $4}'", ct);
            if (long.TryParse(r.StdOut.Trim(), out var kb))
                return kb * 1024;
        }
        catch { }
        return null;
    }

    public sealed record PostRestoreCheck(
        string Package,
        bool IsInstalled,
        long DataSizeBytes,
        bool LaunchedOk,
        string LogcatErrors);

    public async Task<List<PostRestoreCheck>> ValidateRestoredPackagesAsync(
        string emuSerial,
        IReadOnlyList<string> packages,
        CancellationToken ct)
    {
        var results = new List<PostRestoreCheck>();
        foreach (var pkg in packages)
        {
            ct.ThrowIfCancellationRequested();
            if (!AdbService.IsSafeAndroidPackageName(pkg)) continue;

            var installed = false;
            try
            {
                var paths = await _adb.PackagePathsAsync(emuSerial, pkg, ct);
                installed = paths.Count > 0;
            }
            catch { }

            long dataSize = 0;
            try { dataSize = await _adb.DataSizeAsync(emuSerial, pkg, ct); } catch { }

            bool launchedOk = false;
            string logcatErrors = "";
            try
            {
                await _adb.ShellAsync(emuSerial, $"am force-stop {Q(pkg)}", ct);
                await Task.Delay(500, ct);
                await _adb.ShellAsync(emuSerial, $"logcat -c", ct);

                var launch = await _adb.ShellAsync(emuSerial,
                    $"monkey -p {Q(pkg)} -c android.intent.category.LAUNCHER 1 2>&1", ct);
                launchedOk = launch.Success && !launch.Combined.Contains("No activities found");

                await Task.Delay(2000, ct);

                var logcat = await _adb.ShellAsync(emuSerial,
                    $"logcat -d -s AndroidRuntime:E ActivityManager:E -v brief | head -20", ct);
                var errors = logcat.StdOut
                    .Split('\n')
                    .Where(l => l.Contains(pkg, StringComparison.OrdinalIgnoreCase)
                             || l.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
                             || l.Contains("crash", StringComparison.OrdinalIgnoreCase))
                    .Take(5);
                logcatErrors = string.Join("\n", errors).Trim();
            }
            catch { }

            results.Add(new PostRestoreCheck(pkg, installed, dataSize, launchedOk, logcatErrors));
            var status = installed ? (launchedOk ? "ok" : "launch failed") : "not installed";
            _log.Detail($"Post-restore: {pkg} — {status}, data {dataSize / 1024} KB" +
                        (string.IsNullOrEmpty(logcatErrors) ? "" : $", errors: {logcatErrors[..Math.Min(80, logcatErrors.Length)]}"));
        }
        return results;
    }

    public static MigrationReceipt BuildReceipt(
        string sourceSerial,
        string targetSerial,
        IReadOnlyList<string> scopes,
        IReadOnlyList<MigrationPackageReceipt> packages,
        bool cancelled)
    {
        var totalBytes = packages.SelectMany(p => p.Legs).Sum(l => l.SizeBytes);
        var successCount = packages.Count(p => p.Success);
        var failCount = packages.Count(p => !p.Success);
        return new MigrationReceipt
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            SourceSerial = sourceSerial,
            TargetSerial = targetSerial,
            Scopes = scopes.ToList(),
            Packages = packages.ToList(),
            TotalBytes = totalBytes,
            SuccessCount = successCount,
            FailCount = failCount,
            Cancelled = cancelled,
        };
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

    private static string Q(string value) => AdbService.ShellQuote(value);
}
