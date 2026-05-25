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

        try
        {
            var args = new[] { "/c", _sdk.SdkManagerBat, "--licenses" };
            var ys = Enumerable.Repeat("y", 60); // covers all current Google + Android licenses with headroom
            var r = await ProcessRunner.RunWithStdinAsync("cmd.exe", args, ys,
                extraEnv: NoPathConv,
                timeout: TimeSpan.FromMinutes(3),
                ct: ct);
            if (r.Success) { _log.Success("All SDK licenses accepted."); return true; }
            _log.Warning($"sdkmanager --licenses exited {r.ExitCode}: {r.Combined.Trim()}");
            return false;
        }
        catch (OperationCanceledException)
        {
            _log.Error("sdkmanager --licenses timed out / cancelled.");
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

        try
        {
            var args = new List<string> { "/c", _sdk.SdkManagerBat };
            args.AddRange(pkgs);
            var ys = Enumerable.Repeat("y", 30);
            var r = await ProcessRunner.RunWithStdinAsync("cmd.exe", args, ys,
                extraEnv: NoPathConv,
                timeout: TimeSpan.FromMinutes(15),
                ct: ct);
            return r.Success;
        }
        catch (OperationCanceledException)
        {
            _log.Error($"sdkmanager install timed out / cancelled ({string.Join(", ", pkgs)}).");
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
