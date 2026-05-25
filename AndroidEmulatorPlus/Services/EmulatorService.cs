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

    public Process Launch(string avdName, bool coldBoot = false, bool wipeData = false)
    {
        var args = new List<string> { "-avd", avdName };
        if (coldBoot) args.Add("-no-snapshot-load");
        if (wipeData) args.Add("-wipe-data");
        _log.Info($"Launching emulator '{avdName}'{(coldBoot ? " (cold boot)" : "")}{(wipeData ? " (wipe data)" : "")}…");
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
