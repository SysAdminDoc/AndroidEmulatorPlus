using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidEmulatorPlus.Models;

namespace AndroidEmulatorPlus.Services;

public sealed class AvdTemplate
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("systemImage")] public string SystemImage { get; init; } = "";
    [JsonPropertyName("device")] public string Device { get; init; } = "pixel_7";
    [JsonPropertyName("config")] public Dictionary<string, string> Config { get; init; } = new();
    [JsonPropertyName("createdUtc")] public string CreatedUtc { get; init; } = "";
}

public sealed class AvdTemplateService
{
    private readonly AvdService _avds;
    private readonly LogService _log;

    public AvdTemplateService(AvdService avds, LogService log)
    {
        _avds = avds;
        _log = log;
    }

    public static string TemplateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "templates");

    public AvdTemplate ExportTemplate(Avd avd, string templateName, string description = "")
    {
        var relevantKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hw.ramSize", "hw.cpu.ncore", "hw.lcd.width", "hw.lcd.height",
            "hw.lcd.density", "hw.gpu.mode", "hw.gpu.enabled",
            "disk.dataPartition.size", "fastboot.forceFastBoot", "fastboot.forceColdBoot",
            "hw.keyboard", "hw.camera.front", "hw.camera.back",
        };

        var config = new Dictionary<string, string>();
        foreach (var kv in avd.Config)
        {
            if (relevantKeys.Contains(kv.Key))
                config[kv.Key] = kv.Value;
        }

        return new AvdTemplate
        {
            Name = templateName,
            Description = description,
            SystemImage = avd.SystemImage ?? "",
            Device = "pixel_7",
            Config = config,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
        };
    }

    public string SaveTemplate(AvdTemplate template)
    {
        Directory.CreateDirectory(TemplateDirectory);
        var safeName = SanitizeFileName(template.Name);
        var path = Path.Combine(TemplateDirectory, $"{safeName}.json");
        var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        _log.Success($"Template saved: {path}");
        return path;
    }

    public List<AvdTemplate> ListTemplates()
    {
        var list = new List<AvdTemplate>();
        if (!Directory.Exists(TemplateDirectory)) return list;
        foreach (var file in Directory.EnumerateFiles(TemplateDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var t = JsonSerializer.Deserialize<AvdTemplate>(json);
                if (t is not null) list.Add(t);
            }
            catch { }
        }
        return list;
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unnamed";
        var safe = name.Trim().Replace(' ', '_');
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        while (safe.Contains(".."))
            safe = safe.Replace("..", "_");
        if (string.IsNullOrWhiteSpace(safe)) return "unnamed";
        return safe;
    }

    public async Task<bool> ApplyTemplateAsync(string avdName, AvdTemplate template, CancellationToken ct = default)
    {
        if (!AvdService.IsSafeAvdName(avdName))
        {
            _log.Error("Invalid AVD name.");
            return false;
        }

        var image = template.SystemImage;
        if (string.IsNullOrEmpty(image))
        {
            _log.Error("Template has no system image specified.");
            return false;
        }

        var r = await _avds.CreateAsync(avdName, image, template.Device);
        if (!r.Success)
        {
            _log.Error("AVD creation failed: " + r.Combined.Trim());
            return false;
        }

        if (template.Config.Count > 0)
        {
            var avd = _avds.List().FirstOrDefault(a => a.Name.Equals(avdName, StringComparison.OrdinalIgnoreCase));
            if (avd is not null)
                AvdService.WriteIni(avd.ConfigPath, template.Config);
        }

        _log.Success($"AVD '{avdName}' created from template '{template.Name}'.");
        return true;
    }
}
