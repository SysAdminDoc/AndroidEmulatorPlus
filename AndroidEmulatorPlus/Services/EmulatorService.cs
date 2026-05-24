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

    public async Task<List<string>> ListAvdsAsync(CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync(_sdk.EmulatorRequired, new[] { "-list-avds" }, ct: ct);
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    public void TryKill()
    {
        try
        {
            if (_current != null && !_current.HasExited) _current.Kill(entireProcessTree: true);
        }
        catch { }
    }
}
