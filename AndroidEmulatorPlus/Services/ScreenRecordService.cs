using System.IO;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// Drives `adb shell screenrecord` and pulls the result on stop. The emulator's
/// /sdcard is writable by shell; this service owns the lifetime of one in-flight
/// recording (no concurrent recordings).
///
/// Note: <c>screenrecord</c> has a 3-minute hard cap per file on stock Android.
/// Long captures need to be chained externally; this service intentionally does
/// not work around that limit.
/// </summary>
public sealed class ScreenRecordService : IDisposable
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;
    private readonly AdbService _adb;

    private ProcessRunner.RunningProcess? _proc;
    private string? _remotePath;
    private string? _serial;

    public bool IsRecording => _proc?.IsRunning == true;

    public ScreenRecordService(SdkLocator sdk, LogService log, AdbService adb)
    {
        _sdk = sdk;
        _log = log;
        _adb = adb;
    }

    /// <summary>Starts recording. Returns the remote path on success, null on failure.</summary>
    public string? Start(string serial)
    {
        if (IsRecording) { _log.Warning("Already recording."); return null; }
        if (_sdk.AdbExe is null) { _log.Error("adb.exe not found."); return null; }

        var remote = $"/sdcard/aep-rec-{DateTime.Now:yyyyMMdd-HHmmss}.mp4";
        try
        {
            _proc = ProcessRunner.StartStreaming(
                _sdk.AdbExe,
                new[] { "-s", serial, "shell", "screenrecord", remote },
                onStdOut: line => _log.Detail("screenrecord: " + line),
                onStdErr: line => _log.Detail("screenrecord: " + line),
                extraEnv: new Dictionary<string, string?>
                {
                    ["MSYS_NO_PATHCONV"] = "1",
                    ["MSYS2_ARG_CONV_EXCL"] = "*",
                });
            _remotePath = remote;
            _serial = serial;
            _log.Info($"Screen recording started → {remote}");
            return remote;
        }
        catch (Exception ex)
        {
            _log.Error("screenrecord start failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Stops recording (Ctrl+C semantics on the adb process), waits for the file to
    /// flush, and pulls it to <paramref name="destDir"/>. Returns the local path on
    /// success.
    /// </summary>
    public async Task<string?> StopAsync(string destDir, CancellationToken ct = default)
    {
        if (!IsRecording || _proc is null || _remotePath is null || _serial is null)
        {
            _log.Warning("Not recording.");
            return null;
        }
        var remotePath = _remotePath;
        var serial = _serial;

        // adb shell screenrecord traps SIGINT to flush. Sending Ctrl+C on Windows is
        // unreliable; stopping the managed adb process tree closes the device-side
        // stream while awaiting both output readers and process exit.
        var process = _proc;
        _proc = null;
        if (process is not null)
        {
            try { await process.StopAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { _log.Detail("screenrecord stop: " + ex.Message); }
            finally { await process.DisposeAsync().ConfigureAwait(false); }
        }

        try
        {
            // Give the device a moment to finalize the MP4 box.
            await Task.Delay(1500, ct);

            try { Directory.CreateDirectory(destDir); }
            catch (Exception ex)
            {
                _log.Error("recording output folder unavailable: " + ex.Message);
                return null;
            }

            var local = Path.Combine(destDir, Path.GetFileName(remotePath));
            var pull = await _adb.PullAsync(serial, remotePath, local, ct);

            if (!pull.Success || !File.Exists(local))
            {
                _log.Error("screenrecord pull failed: " + pull.Combined.Trim());
                return null;
            }

            var size = new FileInfo(local).Length;
            _log.Success($"Recording saved: {local} ({size / 1024} KB)");
            return local;
        }
        finally
        {
            try { await _adb.ShellAsync(serial, $"rm -f {AdbService.ShellQuote(remotePath)}"); } catch { }
            _remotePath = null;
            _serial = null;
        }
    }

    public void Dispose()
    {
        try { _proc?.Dispose(); } catch { }
        _proc = null;
    }
}
