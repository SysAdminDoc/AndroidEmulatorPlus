using AndroidEmulatorPlus.Helpers;
using AndroidEmulatorPlus.Models;

namespace AndroidEmulatorPlus.Services;

/// <summary>Thin async wrapper around adb.exe. Always disables MSYS path conversion.</summary>
public sealed class AdbService
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

    public Task<ProcessResult> ShellAsync(string serial, string command, CancellationToken ct = default)
        => ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "-s", serial, "shell", command },
            extraEnv: NoPathConv, ct: ct);

    public Task<ProcessResult> RootShellAsync(string serial, string command, CancellationToken ct = default)
        => ShellAsync(serial, $"/debug_ramdisk/su -c '{command.Replace("'", "'\\''")}'", ct);

    public Task<ProcessResult> PullAsync(string serial, string remote, string local, CancellationToken ct = default)
        => ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "-s", serial, "pull", remote, local },
            extraEnv: NoPathConv, ct: ct);

    public Task<ProcessResult> PushAsync(string serial, string local, string remote, CancellationToken ct = default)
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
        => ProcessRunner.RunAsync(_sdk.AdbRequired,
            new[] { "-s", serial, "uninstall", pkg },
            extraEnv: NoPathConv, ct: ct);

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
            .OrderBy(s => s)
            .ToList();
    }

    public async Task<List<string>> PackagePathsAsync(string serial, string pkg, CancellationToken ct = default)
    {
        var r = await ShellAsync(serial, $"pm path {pkg}", ct);
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
}
