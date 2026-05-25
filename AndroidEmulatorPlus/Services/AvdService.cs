using System.IO;
using AndroidEmulatorPlus.Helpers;
using AndroidEmulatorPlus.Models;

namespace AndroidEmulatorPlus.Services;

/// <summary>List, parse, edit AVDs on disk; create/delete via avdmanager.</summary>
public sealed class AvdService
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;

    public AvdService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    public List<Avd> List()
    {
        var list = new List<Avd>();
        if (_sdk.AvdHome is null || !Directory.Exists(_sdk.AvdHome)) return list;

        foreach (var ini in Directory.GetFiles(_sdk.AvdHome, "*.ini"))
        {
            var name = Path.GetFileNameWithoutExtension(ini);
            var avdDir = Path.Combine(_sdk.AvdHome, name + ".avd");
            var configPath = Path.Combine(avdDir, "config.ini");
            if (!File.Exists(configPath)) continue;
            list.Add(new Avd
            {
                Name = name,
                IniPath = ini,
                ConfigPath = configPath,
                Config = ParseIni(configPath),
            });
        }
        return list.OrderBy(a => a.Name).ToList();
    }

    public static Dictionary<string, string> ParseIni(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return dict;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            dict[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return dict;
    }

    public static void WriteIni(string path, IDictionary<string, string> updates)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith('#') || t.StartsWith(';')) continue;
            var eq = t.IndexOf('=');
            if (eq <= 0) continue;
            var key = t[..eq].Trim();
            if (updates.TryGetValue(key, out var v))
            {
                lines[i] = $"{key}={v}";
                seen.Add(key);
            }
        }
        foreach (var kv in updates)
            if (!seen.Contains(kv.Key))
                lines.Add($"{kv.Key}={kv.Value}");
        File.WriteAllLines(path, lines);
    }

    public async Task<List<string>> ListSystemImagesAsync(CancellationToken ct = default)
    {
        var list = new List<string>();
        if (_sdk.SdkRoot is null) return list;
        var sysImgRoot = Path.Combine(_sdk.SdkRoot, "system-images");
        if (!Directory.Exists(sysImgRoot)) return list;

        foreach (var api in Directory.GetDirectories(sysImgRoot))
        foreach (var variant in Directory.GetDirectories(api))
        foreach (var abi in Directory.GetDirectories(variant))
            list.Add($"system-images;{Path.GetFileName(api)};{Path.GetFileName(variant)};{Path.GetFileName(abi)}");
        await Task.CompletedTask;
        return list;
    }

    private static readonly TimeSpan CreateAvdTimeout = TimeSpan.FromMinutes(5);

    public async Task<ProcessResult> CreateAsync(string name, string sysImagePkg, string device = "pixel_7", CancellationToken ct = default)
    {
        if (_sdk.AvdManagerBat is null)
            throw new InvalidOperationException("avdmanager.bat not found. Install cmdline-tools first.");
        var args = new[]
        {
            "create", "avd", "--force",
            "-n", name,
            "-k", sysImagePkg,
            "-d", device,
        };
        _log.Info($"Creating AVD '{name}' with image {sysImagePkg}…");
        // avdmanager.bat prompts for hardware-profile override; pipe "no" via stdin if needed.
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(_sdk.AvdManagerBat);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        await proc.StandardInput.WriteLineAsync("no");
        proc.StandardInput.Close();

        // Cap the run so a hung prompt doesn't lock up the UI.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(CreateAvdTimeout);
        try
        {
            var stdout = await proc.StandardOutput.ReadToEndAsync(linked.Token);
            var stderr = await proc.StandardError.ReadToEndAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token);
            return new ProcessResult(proc.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            _log.Error($"avdmanager create exceeded {CreateAvdTimeout.TotalMinutes:0} min — killed.");
            return new ProcessResult(-1, "", "avdmanager create timed out");
        }
    }

    public Task<ProcessResult> DeleteAsync(string name, CancellationToken ct = default)
    {
        if (_sdk.AvdManagerBat is null)
            throw new InvalidOperationException("avdmanager.bat not found.");
        return ProcessRunner.RunAsync("cmd.exe",
            new[] { "/c", _sdk.AvdManagerBat, "delete", "avd", "-n", name }, ct: ct);
    }
}
