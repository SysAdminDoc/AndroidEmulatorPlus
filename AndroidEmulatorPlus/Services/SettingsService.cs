using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// Per-user app settings persisted to <c>%LOCALAPPDATA%\AndroidEmulatorPlus\settings.json</c>.
///
/// The shape is intentionally flat — fields can be added without a version bump as
/// long as defaults are sensible. Missing keys deserialize to the property default.
/// </summary>
public sealed class AppSettings
{
    /// <summary>"mocha" (dark, default) or "latte" (light).</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "mocha";

    /// <summary>SDK root override; empty/null = autodetect via the usual probe order.</summary>
    [JsonPropertyName("sdkRoot")]
    public string? SdkRootOverride { get; set; }

    /// <summary>Folder screenshots / recordings are written to. Defaults to ~/Pictures/AndroidEmulatorPlus.</summary>
    [JsonPropertyName("mediaDir")]
    public string? MediaDir { get; set; }

    /// <summary>HTTP proxy for the embedded HttpClient (e.g. corporate forward proxy).</summary>
    [JsonPropertyName("httpProxy")]
    public string? HttpProxy { get; set; }

    /// <summary>True once the user has seen / dismissed the first-launch wizard.</summary>
    [JsonPropertyName("hasSeenWizard")]
    public bool HasSeenWizard { get; set; }

    /// <summary>C-16: when true, launch scrcpy automatically after an AVD boots.</summary>
    [JsonPropertyName("autoScrcpy")]
    public bool AutoScrcpy { get; set; }
}

public sealed class SettingsService
{
    private readonly LogService _log;

    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "settings.json");

    public AppSettings Current { get; private set; } = new();

    public SettingsService(LogService log)
    {
        _log = log;
        Load();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(Path)) { Current = new AppSettings(); return; }
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path));
            Current = s ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _log.Warning($"settings.json read failed ({ex.Message}); falling back to defaults.");
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            // C-18: write atomically — settings.json is read by App.OnStartup before
            // any view binds, and a crash mid-write would corrupt it. Write to a
            // sibling .tmp first and rename into place.
            var tmp = Path + ".tmp";
            var payload = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, payload);
            if (File.Exists(Path)) File.Replace(tmp, Path, destinationBackupFileName: null);
            else File.Move(tmp, Path);
        }
        catch (Exception ex)
        {
            _log.Error("settings.json write failed: " + ex.Message);
            try { File.Delete(Path + ".tmp"); } catch { }
        }
    }

    /// <summary>Reads the theme without spinning up the full service — used in App.OnStartup.</summary>
    public static string ReadThemeFromDisk()
    {
        try
        {
            if (!File.Exists(Path)) return "mocha";
            using var doc = JsonDocument.Parse(File.ReadAllText(Path));
            if (doc.RootElement.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString()?.ToLowerInvariant() switch
                {
                    "latte" => "latte",
                    _ => "mocha",
                };
        }
        catch { }
        return "mocha";
    }
}
