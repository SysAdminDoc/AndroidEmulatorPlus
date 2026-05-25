using System.IO;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// Locates and launches an external scrcpy.exe. The app does NOT bundle scrcpy; the
/// user is expected to install it (winget install Genymobile.scrcpy) or drop the
/// executable on PATH or in <c>%LOCALAPPDATA%\AndroidEmulatorPlus\scrcpy\scrcpy.exe</c>.
/// </summary>
public sealed class ScrcpyService
{
    private readonly LogService _log;

    public ScrcpyService(LogService log) => _log = log;

    public string? FindExe()
    {
        // 1. Local override
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidEmulatorPlus", "scrcpy", "scrcpy.exe");
        if (File.Exists(local)) return local;

        // 2. PATH lookup
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            try
            {
                var candidate = Path.Combine(p, "scrcpy.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    public bool IsAvailable => FindExe() is not null;

    public bool Launch(string serial)
    {
        var exe = FindExe();
        if (exe is null)
        {
            _log.Warning("scrcpy.exe not found on PATH. Install via 'winget install Genymobile.scrcpy' or drop scrcpy.exe in %LOCALAPPDATA%\\AndroidEmulatorPlus\\scrcpy\\.");
            return false;
        }
        try
        {
            ProcessRunner.StartDetached(exe, new[] { "-s", serial });
            _log.Info($"scrcpy launched against {serial}.");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("scrcpy launch failed: " + ex.Message);
            return false;
        }
    }
}
