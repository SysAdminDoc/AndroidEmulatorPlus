using System.Diagnostics;
using System.IO;

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

    private Process? _proc;
    private string? _remotePath;
    private string? _serial;

    public bool IsRecording => _proc is { HasExited: false };

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
        var psi = new ProcessStartInfo
        {
            FileName = _sdk.AdbExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var a in new[] { "-s", serial, "shell", "screenrecord", remote }) psi.ArgumentList.Add(a);
        psi.Environment["MSYS_NO_PATHCONV"] = "1";
        psi.Environment["MSYS2_ARG_CONV_EXCL"] = "*";

        try
        {
            _proc = Process.Start(psi);
            if (_proc is null) return null;
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

        // adb shell screenrecord traps SIGINT to flush. Sending Ctrl+C on Windows is
        // unreliable; the safest stop is killing the adb-shell process tree.
        // screenrecord on the device side will close the file on its own when the
        // pipe breaks.
        try { _proc.Kill(entireProcessTree: true); } catch { }
        _proc = null;

        // Give the device a moment to finalize the MP4 box.
        await Task.Delay(1500, ct);

        Directory.CreateDirectory(destDir);
        var local = Path.Combine(destDir, Path.GetFileName(_remotePath));
        var pull = await _adb.PullAsync(_serial, _remotePath, local, ct);
        try { await _adb.ShellAsync(_serial, $"rm -f {_remotePath}", ct); } catch { }

        if (!pull.Success || !File.Exists(local))
        {
            _log.Error("screenrecord pull failed: " + pull.Combined.Trim());
            return null;
        }

        var size = new FileInfo(local).Length;
        _log.Success($"Recording saved: {local} ({size / 1024} KB)");
        _remotePath = null;
        _serial = null;
        return local;
    }

    public void Dispose()
    {
        try { _proc?.Kill(entireProcessTree: true); } catch { }
        _proc = null;
    }
}
