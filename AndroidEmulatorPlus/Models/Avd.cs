namespace AndroidEmulatorPlus.Models;

public sealed class Avd
{
    public required string Name { get; init; }
    public required string ConfigPath { get; init; }     // .../avd/<name>.avd/config.ini
    public required string IniPath { get; init; }        // .../avd/<name>.ini
    public Dictionary<string, string> Config { get; init; } = new();

    public string? SystemImage => Config.GetValueOrDefault("image.sysdir.1");
    public bool PlayStoreEnabled => Config.GetValueOrDefault("PlayStore.enabled", "false")
        .Equals("true", StringComparison.OrdinalIgnoreCase);
    public string? Tag => Config.GetValueOrDefault("tag.id");
    public string? ScreenW => Config.GetValueOrDefault("hw.lcd.width");
    public string? ScreenH => Config.GetValueOrDefault("hw.lcd.height");
    public string? ScreenDpi => Config.GetValueOrDefault("hw.lcd.density");
    public string? RamMB => Config.GetValueOrDefault("hw.ramSize");
    public string? Cores => Config.GetValueOrDefault("hw.cpu.ncore");
    public string? Disk => Config.GetValueOrDefault("disk.dataPartition.size");
    public string? DisplayName => Config.GetValueOrDefault("avd.ini.displayname");

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (ScreenW is not null && ScreenH is not null) parts.Add($"{ScreenW}×{ScreenH}");
            if (ScreenDpi is not null) parts.Add($"{ScreenDpi}dpi");
            if (RamMB is not null) parts.Add($"{RamMB} MB RAM");
            if (Disk is not null) parts.Add($"{Disk} disk");
            if (Cores is not null) parts.Add($"{Cores} cores");
            return string.Join(" · ", parts);
        }
    }

    public string Badge
    {
        get
        {
            if (PlayStoreEnabled) return "Play Store";
            if (Tag is not null && Tag.Contains("google", StringComparison.OrdinalIgnoreCase)) return "Google APIs";
            return Tag ?? "AOSP";
        }
    }
}
