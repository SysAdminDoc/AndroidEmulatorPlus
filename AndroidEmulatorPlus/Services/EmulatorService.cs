using System.Collections.Concurrent;
using System.Diagnostics;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

public sealed class EmulatorService
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;

    /// <summary>
    /// AVD name → emulator child Process. Multiple AVDs can be running concurrently;
    /// previous single-_current field meant a second launch orphaned the first.
    /// </summary>
    private readonly ConcurrentDictionary<string, Process> _children = new(StringComparer.OrdinalIgnoreCase);

    public EmulatorService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    public IReadOnlyCollection<string> RunningAvdNames => _children.Keys.ToArray();

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
        if (!AvdService.IsSafeAvdName(avdName))
            throw new InvalidOperationException("Invalid AVD name.");
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
        var proc = ProcessRunner.StartDetached(_sdk.EmulatorRequired, args,
            workingDir: System.IO.Path.GetDirectoryName(_sdk.EmulatorRequired));
        _children[avdName] = proc;
        proc.Exited += (_, _) => { try { _children.TryRemove(avdName, out _); } catch { } };
        return proc;
    }

    /// <summary>Kills every emulator child this session launched. Called on app exit.</summary>
    public void KillAll()
    {
        foreach (var kv in _children.ToArray())
        {
            try
            {
                if (!kv.Value.HasExited) kv.Value.Kill(entireProcessTree: true);
            }
            catch { }
            _children.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>Kills the emulator child for a single AVD, if we launched one.</summary>
    public void TryKill(string avdName)
    {
        if (_children.TryGetValue(avdName, out var p))
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            _children.TryRemove(avdName, out _);
        }
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
