using System.Diagnostics;
using System.IO;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// Wraps <c>sdkmanager.bat</c>. License acceptance is automated by piping a stream
/// of 'y' lines into stdin (Google's official one-liner for unattended installs).
/// Install / list / update are also exposed here so other view-models can call them
/// without re-implementing the cmd.exe /c plumbing.
/// </summary>
public sealed class SdkmanagerService
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;

    public SdkmanagerService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    private static readonly Dictionary<string, string?> NoPathConv = new()
    {
        ["MSYS_NO_PATHCONV"] = "1",
        ["MSYS2_ARG_CONV_EXCL"] = "*",
    };

    /// <summary>
    /// Runs <c>sdkmanager --licenses</c> and answers 'y' to each prompt up to a generous
    /// cap. Returns true if the process exits 0.
    /// </summary>
    public async Task<bool> AcceptLicensesAsync(IProgress<string>? status, CancellationToken ct = default)
    {
        if (_sdk.SdkManagerBat is null)
        {
            _log.Error("sdkmanager.bat not found. Install cmdline-tools first.");
            return false;
        }
        status?.Report("Running sdkmanager --licenses…");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(_sdk.SdkManagerBat);
        psi.ArgumentList.Add("--licenses");
        foreach (var kv in NoPathConv) psi.Environment[kv.Key] = kv.Value;

        using var proc = Process.Start(psi)!;
        // Pipe a generous supply of "y\n" lines. sdkmanager rotates through every
        // unaccepted license; ~60 covers all current Google + Android licenses with
        // plenty of headroom.
        try
        {
            for (int i = 0; i < 60; i++)
            {
                if (proc.HasExited) break;
                await proc.StandardInput.WriteLineAsync("y");
            }
            proc.StandardInput.Close();
        }
        catch { /* ignored — process may have closed stdin */ }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (proc.ExitCode == 0)
            {
                _log.Success("All SDK licenses accepted.");
                return true;
            }
            _log.Warning($"sdkmanager --licenses exited {proc.ExitCode}: {stdout}{stderr}".Trim());
            return false;
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            _log.Error("sdkmanager --licenses timed out after 3 minutes.");
            return false;
        }
    }

    /// <summary>Installs one or more SDK packages with `y` piped in for any prompts.</summary>
    public async Task<bool> InstallAsync(IEnumerable<string> packages, IProgress<string>? status, CancellationToken ct = default)
    {
        if (_sdk.SdkManagerBat is null)
        {
            _log.Error("sdkmanager.bat not found. Install cmdline-tools first.");
            return false;
        }
        var pkgs = packages.ToList();
        if (pkgs.Count == 0) return true;

        status?.Report($"sdkmanager install: {string.Join(", ", pkgs)}");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(_sdk.SdkManagerBat);
        foreach (var p in pkgs) psi.ArgumentList.Add(p);
        foreach (var kv in NoPathConv) psi.Environment[kv.Key] = kv.Value;

        using var proc = Process.Start(psi)!;
        // Same y-spam pattern.
        try
        {
            for (int i = 0; i < 30; i++)
            {
                if (proc.HasExited) break;
                await proc.StandardInput.WriteLineAsync("y");
            }
            proc.StandardInput.Close();
        }
        catch { }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            await proc.WaitForExitAsync(linked.Token);
            return proc.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            _log.Error($"sdkmanager install timed out after 15 minutes ({string.Join(", ", pkgs)}).");
            return false;
        }
    }

    /// <summary>
    /// Returns the list of "Available Packages" reported by <c>sdkmanager --list --no_https</c>
    /// (system-images and emulator targets). Items are full package paths like
    /// <c>system-images;android-36;google_apis_playstore;x86_64</c>.
    /// </summary>
    public async Task<List<string>> ListAvailableSystemImagesAsync(CancellationToken ct = default)
    {
        if (_sdk.SdkManagerBat is null) return new();
        var r = await ProcessRunner.RunAsync("cmd.exe",
            new[] { "/c", _sdk.SdkManagerBat, "--list" },
            extraEnv: NoPathConv,
            timeout: TimeSpan.FromMinutes(3),
            ct: ct);
        var list = new List<string>();
        bool inAvailable = false;
        foreach (var raw in r.StdOut.Split('\n'))
        {
            var line = raw.Trim().Replace("\r", "");
            if (line.StartsWith("Available Packages", StringComparison.OrdinalIgnoreCase)) { inAvailable = true; continue; }
            if (line.StartsWith("Available Updates", StringComparison.OrdinalIgnoreCase)) { inAvailable = false; continue; }
            if (!inAvailable) continue;
            // sdkmanager output rows look like:  system-images;android-36;google_apis_playstore;x86_64 | 1 | Google Play …
            if (!line.StartsWith("system-images;", StringComparison.OrdinalIgnoreCase)) continue;
            var pkg = line.Split('|')[0].Trim();
            if (pkg.Length > 0) list.Add(pkg);
        }
        return list;
    }
}
