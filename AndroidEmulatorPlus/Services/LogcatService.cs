using AndroidEmulatorPlus.Helpers;

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
    private ProcessRunner.RunningProcess? _proc;
    private CancellationTokenSource? _cts;

    public event Action<string>? LineReceived;

    public LogcatService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    public bool IsRunning => _proc?.IsRunning == true;

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

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            _proc = ProcessRunner.StartStreaming(
                _sdk.AdbExe,
                args,
                onStdOut: line => { if (!ct.IsCancellationRequested) LineReceived?.Invoke(line); },
                onStdErr: line => { if (!ct.IsCancellationRequested) LineReceived?.Invoke(line); },
                extraEnv: new Dictionary<string, string?>
                {
                    ["MSYS_NO_PATHCONV"] = "1",
                    ["MSYS2_ARG_CONV_EXCL"] = "*",
                });
        }
        catch (Exception ex)
        {
            _log.Error("logcat start failed: " + ex.Message);
            _proc = null;
            _cts.Dispose();
            _cts = null;
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

    public async Task StopAsync(CancellationToken ct = default)
    {
        try { _cts?.Cancel(); } catch { }
        var process = _proc;
        _proc = null;
        if (process is not null)
        {
            try { await process.StopAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { _log.Detail("logcat stop: " + ex.Message); }
            finally { await process.DisposeAsync().ConfigureAwait(false); }
        }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }

    public void Stop()
    {
        try { StopAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { _log.Detail("logcat stop: " + ex.Message); }
    }

    public void Dispose() => Stop();
}
