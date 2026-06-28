using AndroidEmulatorPlus.Helpers;
using AndroidEmulatorPlus.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AndroidEmulatorPlus.Services;

/// <summary>Thin async wrapper around adb.exe. Always disables MSYS path conversion.</summary>
public class AdbService
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;

    public AdbService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    private static readonly Dictionary<string, string?> NoPathConv = new()
    {
        ["MSYS_NO_PATHCONV"] = "1",
        ["MSYS2_ARG_CONV_EXCL"] = "*",
    };

    public Task<ProcessResult> RawAsync(IEnumerable<string> args, CancellationToken ct = default)
        => ProcessRunner.RunAsync(_sdk.AdbRequired, args, extraEnv: NoPathConv, ct: ct);

    public async Task<List<Device>> ListDevicesAsync(CancellationToken ct = default)
    {
        var result = await RawAsync(new[] { "devices", "-l" }, ct);
        var list = new List<Device>();
        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("List of") || string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var serial = parts[0];
            var state = parts[1];
            string model = "", product = "";
            foreach (var kv in parts.Skip(2))
            {
                if (kv.StartsWith("model:")) model = kv[6..];
                else if (kv.StartsWith("product:")) product = kv[8..];
            }
            list.Add(new Device(serial, state, model, product, serial.StartsWith("emulator-")));
        }
        return list;
    }

    public async Task<DeviceDiagnostics> GetDeviceDiagnosticsAsync(Device device, CancellationToken ct = default)
    {
        var platformTools = await PlatformToolsVersionAsync(ct);
        if (!device.IsOnline)
        {
            return BuildDiagnostics(device, platformTools, apiLevel: null, securityPatch: null, DateTime.UtcNow);
        }

        string? apiLevel = null;
        string? securityPatch = null;
        try
        {
            var props = await ShellAsync(device.Serial,
                "getprop ro.build.version.sdk; getprop ro.build.version.security_patch", ct);
            (apiLevel, securityPatch) = ParseDeviceBuildProps(props.StdOut);
        }
        catch { }

        return BuildDiagnostics(device, platformTools, apiLevel, securityPatch, DateTime.UtcNow);
    }

    public async Task<string?> PlatformToolsVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await RawAsync(new[] { "version" }, ct);
            return ParsePlatformToolsVersion(r.StdOut);
        }
        catch { return null; }
    }

    public static string InferTransport(Device device)
    {
        if (device.IsEmulator) return "emulator";
        return device.Serial.Contains(':', StringComparison.Ordinal) ? "wireless" : "usb";
    }

    public static string? ParsePlatformToolsVersion(string stdout)
    {
        foreach (var raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            const string prefix = "Android Debug Bridge version ";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line[prefix.Length..].Trim();
            if (line.StartsWith("Version ", StringComparison.OrdinalIgnoreCase))
                return line["Version ".Length..].Trim();
        }
        return null;
    }

    public static (string? ApiLevel, string? SecurityPatch) ParseDeviceBuildProps(string stdout)
    {
        var lines = stdout.Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None)
            .Select(static line => line.Trim())
            .ToArray();
        var api = lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]) ? lines[0] : null;
        var patch = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1] : null;
        return (api, patch);
    }

    public static bool IsSecurityPatchStale(string? patch, DateTime utcNow, int maxAgeDays = 120)
    {
        if (!DateOnly.TryParseExact(patch, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var patchDate))
            return false;
        var age = DateOnly.FromDateTime(utcNow.Date).DayNumber - patchDate.DayNumber;
        return age > maxAgeDays;
    }

    private static DeviceDiagnostics BuildDiagnostics(
        Device device,
        string? platformToolsVersion,
        string? apiLevel,
        string? securityPatch,
        DateTime utcNow)
    {
        var transport = InferTransport(device);
        var patchUnknown = string.IsNullOrWhiteSpace(securityPatch);
        var patchStale = IsSecurityPatchStale(securityPatch, utcNow);
        var api = string.IsNullOrWhiteSpace(apiLevel) ? "unknown" : apiLevel;
        var patch = patchUnknown ? "unknown" : securityPatch!;
        var tools = string.IsNullOrWhiteSpace(platformToolsVersion) ? "unknown" : platformToolsVersion;
        var warning = patchUnknown ? "patch unknown" : patchStale ? "patch stale" : "patch current";
        var summary = $"{transport}; API {api}; security patch {patch} ({warning}); platform-tools {tools}";
        return new DeviceDiagnostics(device.Serial, transport, apiLevel, securityPatch, platformToolsVersion,
            patchUnknown, patchStale, summary);
    }

    public virtual Task<ProcessResult> ShellAsync(string serial, string command, CancellationToken ct = default)
        => ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "-s", serial, "shell", command },
            extraEnv: NoPathConv, ct: ct);

    public virtual Task<ProcessResult> RootShellAsync(string serial, string command, CancellationToken ct = default)
        => ShellAsync(serial, $"/debug_ramdisk/su -c {ShellQuote(command)}", ct);

    public static string ShellQuote(string value)
        => "'" + value.Replace("'", "'\\''") + "'";

    public static bool IsSafeAndroidPackageName(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 255
           && Regex.IsMatch(value, @"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*$");

    public static bool IsSafeMagiskModuleId(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 128
           && Regex.IsMatch(value, @"^[A-Za-z0-9._-]+$");

    public virtual Task<ProcessResult> PullAsync(string serial, string remote, string local, CancellationToken ct = default)
        => ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "-s", serial, "pull", remote, local },
            extraEnv: NoPathConv, ct: ct);

    public virtual Task<ProcessResult> PushAsync(string serial, string local, string remote, CancellationToken ct = default)
        => ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "-s", serial, "push", local, remote },
            extraEnv: NoPathConv, ct: ct);

    public Task<ProcessResult> InstallAsync(string serial, IEnumerable<string> apkPaths, CancellationToken ct = default)
    {
        var apks = apkPaths.ToList();
        var args = new List<string> { "-s", serial };
        args.Add(apks.Count > 1 ? "install-multiple" : "install");
        args.AddRange(new[] { "-r", "-d", "-g" });
        args.AddRange(apks);
        return ProcessRunner.RunAsync(_sdk.AdbRequired, args, extraEnv: NoPathConv, ct: ct);
    }

    public Task<ProcessResult> UninstallAsync(string serial, string pkg, CancellationToken ct = default)
        => IsSafeAndroidPackageName(pkg)
            ? ProcessRunner.RunAsync(_sdk.AdbRequired,
                new[] { "-s", serial, "uninstall", pkg },
                extraEnv: NoPathConv, ct: ct)
            : Task.FromResult(new ProcessResult(-1, "", "invalid package name"));

    public async Task<bool> WaitForBootAsync(string serial, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var r = await ShellAsync(serial, "getprop sys.boot_completed", ct);
            if (r.StdOut.Trim() == "1") return true;
            await Task.Delay(2000, ct);
        }
        return false;
    }

    public async Task<bool> IsRootedAsync(string serial, CancellationToken ct = default)
    {
        var r = await RootShellAsync(serial, "id", ct);
        return r.Success && r.StdOut.Contains("uid=0");
    }

    public async Task<List<string>> ListPackagesAsync(string serial, bool userOnly = true, CancellationToken ct = default)
    {
        var r = await ShellAsync(serial, userOnly ? "pm list packages -3" : "pm list packages", ct);
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Replace("\r", ""))
            .Where(l => l.StartsWith("package:"))
            .Select(l => l["package:".Length..])
            .Where(IsSafeAndroidPackageName)
            .OrderBy(s => s)
            .ToList();
    }

    /// <summary>
    /// Lists packages with a flavor filter. Returns the set of package names matching the
    /// given <c>pm list packages</c> flag (one of "-3", "-s", "-d", "" for all).
    /// </summary>
    public async Task<HashSet<string>> ListPackagesFlagAsync(string serial, string flag, CancellationToken ct = default)
    {
        if (flag is not ("" or "-3" or "-s" or "-d")) return new HashSet<string>(StringComparer.Ordinal);
        var cmd = string.IsNullOrEmpty(flag) ? "pm list packages" : $"pm list packages {flag}";
        var r = await ShellAsync(serial, cmd, ct);
        return new HashSet<string>(r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Replace("\r", ""))
            .Where(l => l.StartsWith("package:"))
            .Select(l => l["package:".Length..])
            .Where(IsSafeAndroidPackageName),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the on-device data directory size for <paramref name="pkg"/>, in bytes.
    /// Uses <c>du -sb /data/data/&lt;pkg&gt;</c> which requires root. Returns 0 on failure.
    /// </summary>
    public async Task<long> DataSizeAsync(string serial, string pkg, CancellationToken ct = default)
    {
        if (!IsSafeAndroidPackageName(pkg)) return 0;
        var r = await RootShellAsync(serial, $"du -sb {ShellQuote($"/data/data/{pkg}")} 2>/dev/null | awk '{{print $1}}'", ct);
        if (!r.Success) return 0;
        return long.TryParse(r.StdOut.Trim(), out var n) ? n : 0;
    }

    public async Task<List<string>> PackagePathsAsync(string serial, string pkg, CancellationToken ct = default)
    {
        if (!IsSafeAndroidPackageName(pkg)) return new();
        var r = await ShellAsync(serial, $"pm path {ShellQuote(pkg)}", ct);
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Replace("\r", ""))
            .Where(l => l.StartsWith("package:"))
            .Select(l => l["package:".Length..])
            .ToList();
    }

    public async Task KillServerAsync(CancellationToken ct = default)
    {
        try { await RawAsync(new[] { "kill-server" }, ct); } catch { }
        try { await RawAsync(new[] { "start-server" }, ct); } catch { }
    }

    public Task<ProcessResult> EmuKillAsync(string serial, CancellationToken ct = default)
        => ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "-s", serial, "emu", "kill" },
            extraEnv: NoPathConv, ct: ct);

    /// <summary>
    /// Pairs with an Android 11+ phone over Wi-Fi using a host:port + 6-digit code from
    /// Developer options → Wireless debugging → Pair using pairing code. Equivalent
    /// to <c>adb pair host:port code</c>. The pairing port is one-shot; after pairing
    /// succeeds, the phone exposes a separate <i>connect</i> port (also visible in the
    /// Wireless debugging UI) — pass that to <see cref="ConnectAsync"/>.
    /// </summary>
    public async Task<ProcessResult> PairAsync(string hostPort, string code, CancellationToken ct = default)
    {
        // adb pair takes the code as a second positional arg on platform-tools 35+
        // and on stdin on older builds. Supply both: positional + a stdin line.
        try
        {
            return await ProcessRunner.RunWithStdinAsync(_sdk.AdbRequired,
                new[] { "pair", hostPort, code },
                new[] { code },
                extraEnv: NoPathConv,
                timeout: TimeSpan.FromSeconds(30),
                ct: ct);
        }
        catch (TimeoutException)
        {
            return new ProcessResult(-1, "", "adb pair timed out after 30s");
        }
        catch (OperationCanceledException)
        {
            return new ProcessResult(-1, "", "adb pair cancelled");
        }
    }

    /// <summary>Calls <c>adb connect host:port</c>.</summary>
    public Task<ProcessResult> ConnectAsync(string hostPort, CancellationToken ct = default)
        => ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "connect", hostPort },
            extraEnv: NoPathConv, timeout: TimeSpan.FromSeconds(15), ct: ct);

    /// <summary>Calls <c>adb disconnect host:port</c> (or all if omitted).</summary>
    public Task<ProcessResult> DisconnectAsync(string? hostPort = null, CancellationToken ct = default)
    {
        var args = hostPort is null ? new[] { "disconnect" } : new[] { "disconnect", hostPort };
        return ProcessRunner.RunAsync(_sdk.AdbRequired, args, extraEnv: NoPathConv, ct: ct);
    }

    /// <summary>Captures a PNG screenshot from the device and pulls it to <paramref name="destPath"/>.</summary>
    public async Task<bool> ScreenshotAsync(string serial, string destPath, CancellationToken ct = default)
    {
        var remote = $"/sdcard/aep-shot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
        var cap = await ShellAsync(serial, $"screencap -p {ShellQuote(remote)}", ct);
        if (!cap.Success) return false;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath)!);
            var pull = await PullAsync(serial, remote, destPath, ct);
            if (!pull.Success) return false;
        }
        finally
        {
            try { await ShellAsync(serial, $"rm -f {ShellQuote(remote)}", ct); } catch { }
        }
        return System.IO.File.Exists(destPath);
    }

    /// <summary>Best-effort lookup of the AVD name for a running emulator serial.</summary>
    public async Task<string?> AvdNameForSerialAsync(string serial, CancellationToken ct = default)
    {
        // The property name varies slightly across emulator versions; try both.
        foreach (var prop in new[] { "ro.kernel.qemu.avd_name", "ro.boot.qemu.avd_name" })
        {
            try
            {
                var r = await ShellAsync(serial, $"getprop {prop}", ct);
                var v = r.StdOut.Trim();
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch { }
        }
        return null;
    }

}
