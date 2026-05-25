using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndroidEmulatorPlus.Services;

public sealed class BloatPreset
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("packages")] public List<string> Packages { get; set; } = new();
}

internal sealed class PresetFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("presets")] public List<BloatPreset> Presets { get; set; } = new();
}

/// <summary>
/// Loads debloat presets from the embedded resource and merges any user overrides
/// at <c>%LOCALAPPDATA%\AndroidEmulatorPlus\presets\bloat.json</c>. Entries with the
/// same <c>id</c> replace the embedded version; new ids are appended.
/// </summary>
public sealed class PresetService
{
    private readonly LogService _log;
    public IReadOnlyList<BloatPreset> Presets { get; private set; } = Array.Empty<BloatPreset>();

    public static string UserOverridePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "presets", "bloat.json");

    public PresetService(LogService log)
    {
        _log = log;
        Load();
    }

    public void Load()
    {
        var bundled = ReadBundled();
        var byId = bundled.ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase);

        try
        {
            if (File.Exists(UserOverridePath))
            {
                var user = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(UserOverridePath));
                if (user is not null)
                {
                    foreach (var p in user.Presets)
                    {
                        if (string.IsNullOrWhiteSpace(p.Id)) continue;
                        byId[p.Id] = p;
                    }
                    _log.Info($"Loaded {user.Presets.Count} user preset override(s) from {UserOverridePath}.");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not read user preset overrides: {ex.Message}");
        }

        Presets = byId.Values
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<BloatPreset> ReadBundled()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("bloat-presets.json", StringComparison.OrdinalIgnoreCase));
            if (resName is null) return new();
            using var s = asm.GetManifestResourceStream(resName)!;
            var file = JsonSerializer.Deserialize<PresetFile>(s);
            return file?.Presets ?? new();
        }
        catch
        {
            return new();
        }
    }
}
