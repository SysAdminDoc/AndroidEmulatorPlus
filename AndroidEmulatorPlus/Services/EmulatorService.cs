using System.Diagnostics;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

public sealed class EmulatorService
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;
    private Process? _current;

    public EmulatorService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    public sealed record LaunchOptions(
        bool ColdBoot = false,
        bool WipeData = false,
        bool NoWindow = false,
        bool NoAudio = false,
        string? HttpProxy = null,
        string? DnsServer = null,
        string? FrontCamera = null,
        string? BackCamera = null,
        string? GpuMode = null);

    public Process Launch(string avdName, bool coldBoot = false, bool wipeData = false)
        => Launch(avdName, new LaunchOptions(ColdBoot: coldBoot, WipeData: wipeData));

    public Process Launch(string avdName, LaunchOptions opt)
    {
        var args = new List<string> { "-avd", avdName };
        if (opt.ColdBoot) args.Add("-no-snapshot-load");
        if (opt.WipeData) args.Add("-wipe-data");
        if (opt.NoWindow) args.Add("-no-window");
        if (opt.NoAudio) args.Add("-no-audio");
        if (!string.IsNullOrWhiteSpace(opt.HttpProxy)) { args.Add("-http-proxy"); args.Add(opt.HttpProxy!); }
        if (!string.IsNullOrWhiteSpace(opt.DnsServer)) { args.Add("-dns-server"); args.Add(opt.DnsServer!); }
        if (!string.IsNullOrWhiteSpace(opt.FrontCamera)) { args.Add("-camera-front"); args.Add(opt.FrontCamera!); }
        if (!string.IsNullOrWhiteSpace(opt.BackCamera))  { args.Add("-camera-back");  args.Add(opt.BackCamera!); }
        if (!string.IsNullOrWhiteSpace(opt.GpuMode))     { args.Add("-gpu");          args.Add(opt.GpuMode!); }

        var flagSummary = string.Join(" ", args.Skip(2));
        _log.Info($"Launching emulator '{avdName}' {flagSummary}".TrimEnd());
        _current = ProcessRunner.StartDetached(_sdk.EmulatorRequired, args,
            workingDir: System.IO.Path.GetDirectoryName(_sdk.EmulatorRequired));
        return _current;
    }

    public void TryKill()
    {
        try
        {
            if (_current != null && !_current.HasExited) _current.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public sealed record AccelStatus(bool Ok, string Summary, string Detail);

    /// <summary>Runs `emulator -accel-check` and parses the verdict.</summary>
    public async Task<AccelStatus> AccelCheckAsync(CancellationToken ct = default)
    {
        if (_sdk.EmulatorExe is null) return new AccelStatus(false, "emulator.exe not found", "");
        var r = await ProcessRunner.RunAsync(_sdk.EmulatorExe, new[] { "-accel-check" },
            timeout: TimeSpan.FromSeconds(30), ct: ct);
        var combined = r.Combined.Trim();
        // First non-empty line that is not the literal "accel:" / "accel" prelude is usually
        // a human-readable verdict (e.g. "Hyper-V is enabled" or "HAXM is not installed").
        var summary = combined
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.Equals("accel:", StringComparison.OrdinalIgnoreCase) && !l.Equals("accel", StringComparison.OrdinalIgnoreCase))
            ?? "no output";
        return new AccelStatus(r.ExitCode == 0, summary, combined);
    }
}
