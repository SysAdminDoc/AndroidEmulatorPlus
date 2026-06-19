using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndroidEmulatorPlus.Services;

public sealed class NetworkProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("speed")] public string Speed { get; set; } = "full";
    [JsonPropertyName("delay")] public string Delay { get; set; } = "none";

    public override string ToString() => Name;
}

internal sealed class NetworkProfileFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("profiles")] public List<NetworkProfile> Profiles { get; set; } = new();
}

/// <summary>
/// Loads network condition profiles from the embedded resource and merges any user
/// overrides at <c>%LOCALAPPDATA%\AndroidEmulatorPlus\presets\network-profiles.json</c>.
/// </summary>
public sealed class NetworkProfileService
{
    private readonly LogService _log;
    public IReadOnlyList<NetworkProfile> Profiles { get; private set; } = Array.Empty<NetworkProfile>();

    public static string UserOverridePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "presets", "network-profiles.json");

    public NetworkProfileService(LogService log)
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
                var user = JsonSerializer.Deserialize<NetworkProfileFile>(File.ReadAllText(UserOverridePath));
                if (user is not null)
                {
                    foreach (var p in user.Profiles)
                    {
                        if (string.IsNullOrWhiteSpace(p.Id)) continue;
                        byId[p.Id] = p;
                    }
                    _log.Info($"Loaded {user.Profiles.Count} user network profile override(s).");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not read user network profile overrides: {ex.Message}");
        }

        Profiles = byId.Values
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<NetworkProfile> ReadBundled()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("network-profiles.json", StringComparison.OrdinalIgnoreCase));
            if (resName is null) return new();
            using var s = asm.GetManifestResourceStream(resName)!;
            var file = JsonSerializer.Deserialize<NetworkProfileFile>(s);
            return file?.Profiles ?? new();
        }
        catch
        {
            return new();
        }
    }
}
