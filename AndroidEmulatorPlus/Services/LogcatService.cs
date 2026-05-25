using System.Diagnostics;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// Streams `adb -s &lt;serial&gt; logcat -v threadtime` and pushes parsed lines to the
/// view-model. One stream per service instance; the caller is expected to call
/// <see cref="Stop"/> before starting a new stream.
/// </summary>
public sealed class LogcatService : IDisposable
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;
    private Process? _proc;
    private CancellationTokenSource? _cts;

    public event Action<string>? LineReceived;

    public LogcatService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    public bool IsRunning => _proc is { HasExited: false };

    /// <summary>
    /// Starts streaming logcat for the given device. <paramref name="filter"/> is
    /// passed verbatim as extra args (e.g. "*:E" or "com.example.app:V *:S").
    /// </summary>
    public void Start(string serial, string? filter = null)
    {
        Stop();
        if (_sdk.AdbExe is null) { _log.Error("logcat: adb.exe not found"); return; }

        var args = new List<string> { "-s", serial, "logcat", "-v", "threadtime" };
        if (!string.IsNullOrWhiteSpace(filter))
            foreach (var tok in filter.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(tok);

        var psi = new ProcessStartInfo
        {
            FileName = _sdk.AdbExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["MSYS_NO_PATHCONV"] = "1";
        psi.Environment["MSYS2_ARG_CONV_EXCL"] = "*";

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _proc.OutputDataReceived += (_, e) =>
        {
            if (!ct.IsCancellationRequested && e.Data is not null) LineReceived?.Invoke(e.Data);
        };
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (!ct.IsCancellationRequested && e.Data is not null) LineReceived?.Invoke(e.Data);
        };

        try
        {
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            _log.Error("logcat start failed: " + ex.Message);
            _proc = null;
        }
    }

    /// <summary>Calls <c>adb logcat -c</c> to clear the on-device ring buffer.</summary>
    public async Task ClearBufferAsync(string serial, CancellationToken ct = default)
    {
        if (_sdk.AdbExe is null) return;
        var r = await Helpers.ProcessRunner.RunAsync(_sdk.AdbExe,
            new[] { "-s", serial, "logcat", "-c" },
            extraEnv: new Dictionary<string, string?> { ["MSYS_NO_PATHCONV"] = "1", ["MSYS2_ARG_CONV_EXCL"] = "*" }, ct: ct);
        if (r.Success) _log.Info("logcat buffer cleared.");
        else _log.Warning("logcat -c: " + r.Combined.Trim());
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true);
        }
        catch { }
        _proc = null;
        _cts = null;
    }

    public void Dispose() => Stop();
}
