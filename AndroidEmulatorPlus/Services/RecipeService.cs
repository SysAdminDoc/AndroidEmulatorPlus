using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

public sealed class RecipeStep
{
    [JsonPropertyName("action")] public string Action { get; init; } = "";
    [JsonPropertyName("args")] public Dictionary<string, string> Args { get; init; } = new();
    [JsonPropertyName("description")] public string Description { get; init; } = "";
}

public sealed class Recipe
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("createdUtc")] public string CreatedUtc { get; init; } = "";
    [JsonPropertyName("steps")] public List<RecipeStep> Steps { get; init; } = [];
}

public sealed record RecipeRunResult(int Completed, int Failed, int Skipped, List<string> Log);

public sealed class RecipeService
{
    private readonly AdbService _adb;
    private readonly EmulatorService _emu;
    private readonly AppService _apps;
    private readonly ConsoleService _console;
    private readonly LogService _log;

    public RecipeService(AdbService adb, EmulatorService emu, AppService apps, ConsoleService console, LogService log)
    {
        _adb = adb;
        _emu = emu;
        _apps = apps;
        _console = console;
        _log = log;
    }

    public static string RecipeDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "recipes");

    public string Save(Recipe recipe)
    {
        Directory.CreateDirectory(RecipeDirectory);
        var safeName = AvdTemplateService.SanitizeFileName(recipe.Name);
        var path = Path.Combine(RecipeDirectory, $"{safeName}.json");
        var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        _log.Success($"Recipe saved: {path}");
        return path;
    }

    public List<Recipe> List()
    {
        var list = new List<Recipe>();
        if (!Directory.Exists(RecipeDirectory)) return list;
        foreach (var file in Directory.EnumerateFiles(RecipeDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var r = JsonSerializer.Deserialize<Recipe>(json);
                if (r is not null) list.Add(r);
            }
            catch { }
        }
        return list;
    }

    public async Task<RecipeRunResult> RunAsync(Recipe recipe, string serial, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var log = new List<string>();
        int completed = 0, failed = 0, skipped = 0;

        for (int i = 0; i < recipe.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = recipe.Steps[i];
            var desc = $"[{i + 1}/{recipe.Steps.Count}] {step.Action}: {step.Description}";
            progress?.Report(desc);
            _log.Info(desc);

            try
            {
                var ok = await ExecuteStepAsync(step, serial, ct);
                if (ok)
                {
                    completed++;
                    log.Add($"OK: {desc}");
                }
                else
                {
                    failed++;
                    log.Add($"FAIL: {desc}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                log.Add($"ERROR: {desc} — {ex.Message}");
                _log.Warning($"Step failed: {ex.Message}");
            }
        }

        return new RecipeRunResult(completed, failed, skipped, log);
    }

    private async Task<bool> ExecuteStepAsync(RecipeStep step, string serial, CancellationToken ct)
    {
        switch (step.Action.ToLowerInvariant())
        {
            case "launch":
            {
                var avdName = step.Args.GetValueOrDefault("avd") ?? "";
                if (string.IsNullOrWhiteSpace(avdName)) return false;
                _emu.Launch(avdName);
                if (step.Args.ContainsKey("waitBoot"))
                    await _adb.WaitForBootAsync(serial, TimeSpan.FromMinutes(3), ct);
                return true;
            }

            case "install":
            {
                var apkPath = step.Args.GetValueOrDefault("path") ?? "";
                if (!File.Exists(apkPath)) { _log.Warning($"APK not found: {apkPath}"); return false; }
                var r = await _adb.InstallAsync(serial, new[] { apkPath }, ct);
                return r.Success || r.Combined.Contains("Success");
            }

            case "push":
            {
                var localPath = step.Args.GetValueOrDefault("local") ?? "";
                var remotePath = step.Args.GetValueOrDefault("remote") ?? "/sdcard/Download/";
                if (!File.Exists(localPath)) { _log.Warning($"File not found: {localPath}"); return false; }
                if (remotePath.EndsWith('/'))
                    remotePath += Path.GetFileName(localPath);
                var r = await _adb.PushAsync(serial, localPath, remotePath, ct);
                return r.Success;
            }

            case "shell":
            {
                var cmd = step.Args.GetValueOrDefault("command") ?? "";
                if (string.IsNullOrWhiteSpace(cmd)) return false;
                var r = await _adb.ShellAsync(serial, cmd, ct);
                return r.Success;
            }

            case "console":
            {
                var cmd = step.Args.GetValueOrDefault("command") ?? "";
                if (string.IsNullOrWhiteSpace(cmd)) return false;
                var args = ConsoleService.ParseEmuArgs(cmd);
                var r = await _console.SendAsync(serial, args, ct);
                return r.Success;
            }

            case "uninstall":
            {
                var pkg = step.Args.GetValueOrDefault("package") ?? "";
                if (!AdbService.IsSafeAndroidPackageName(pkg)) return false;
                var r = await _adb.UninstallAsync(serial, pkg, ct);
                return r.Success || r.Combined.Contains("Success");
            }

            case "wait":
            {
                var seconds = int.TryParse(step.Args.GetValueOrDefault("seconds"), out var s) ? s : 5;
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300)), ct);
                return true;
            }

            default:
                _log.Warning($"Unknown recipe action: {step.Action}");
                return false;
        }
    }
}
