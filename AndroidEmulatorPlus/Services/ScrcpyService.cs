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

    public bool Launch(string serial, IEnumerable<string>? extraArgs = null)
    {
        var exe = FindExe();
        if (exe is null)
        {
            _log.Warning("scrcpy.exe not found on PATH. Install via 'winget install Genymobile.scrcpy' or drop scrcpy.exe in %LOCALAPPDATA%\\AndroidEmulatorPlus\\scrcpy\\.");
            return false;
        }
        try
        {
            var args = new List<string> { "-s", serial };
            if (extraArgs is not null) args.AddRange(extraArgs);
            var proc = ProcessRunner.StartDetached(exe, args);
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) => { try { proc.Dispose(); } catch { } };
            _log.Info($"scrcpy launched against {serial} with args: {string.Join(" ", args)}.");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("scrcpy launch failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Detects whether the installed scrcpy is version 4.0+ by running <c>scrcpy --version</c>.
    /// Returns the parsed version, or null if detection fails.
    /// </summary>
    public async Task<Version?> DetectVersionAsync()
    {
        var exe = FindExe();
        if (exe is null) return null;
        try
        {
            var result = await ProcessRunner.RunAsync(exe, new[] { "--version" },
                timeout: TimeSpan.FromSeconds(5));
            if (!result.Success) return null;
            // scrcpy --version outputs something like "scrcpy 4.0 ..." or "scrcpy 3.1 ..."
            var rx = new System.Text.RegularExpressions.Regex(@"scrcpy\s+(\d+\.\d+(\.\d+)?)");
            var match = rx.Match(result.StdOut);
            if (match.Success && Version.TryParse(match.Groups[1].Value, out var v))
                return v;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Builds extra arguments for scrcpy 4.0+ features. Returns an empty list if the
    /// installed version is older or detection fails.
    /// </summary>
    public static IReadOnlyList<string> BuildScrcpy4Args(Version? version, bool flexDisplay = false)
    {
        var args = new List<string>();
        if (version is null || version < new Version(4, 0)) return args;
        if (flexDisplay) args.Add("--display-mode=flex");
        return args;
    }
}
