using System.Collections.ObjectModel;
using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class ConfigViewModel : ObservableObject
{
    private readonly AvdService _avds;
    private readonly ConfigService _cfg;
    private readonly LogService _log;

    public ObservableCollection<Avd> Avds { get; } = new();
    [ObservableProperty] private Avd? _selected;
    [ObservableProperty] private int _ramMb;
    [ObservableProperty] private int _cores;
    [ObservableProperty] private int _diskGb;
    [ObservableProperty] private int _screenW;
    [ObservableProperty] private int _screenH;
    [ObservableProperty] private int _dpi;
    [ObservableProperty] private bool _fastBoot;
    [ObservableProperty] private bool _coldBootAlways;
    [ObservableProperty] private bool _isBusy;

    public ConfigViewModel(AvdService avds, ConfigService cfg, LogService log)
    {
        _avds = avds;
        _cfg = cfg;
        _log = log;
    }

    [RelayCommand]
    private void Refresh()
    {
        Avds.Clear();
        foreach (var a in _avds.List()) Avds.Add(a);
        Selected ??= Avds.FirstOrDefault();
        LoadFromSelected();
    }

    partial void OnSelectedChanged(Avd? value) => LoadFromSelected();

    private void LoadFromSelected()
    {
        if (Selected is null) return;
        RamMb = int.TryParse(Selected.RamMB, out var r) ? r : 2048;
        Cores = int.TryParse(Selected.Cores, out var c) ? c : 4;
        DiskGb = ParseSizeGb(Selected.Disk) ?? 16;
        ScreenW = int.TryParse(Selected.ScreenW, out var w) ? w : 1080;
        ScreenH = int.TryParse(Selected.ScreenH, out var h) ? h : 2400;
        Dpi = int.TryParse(Selected.ScreenDpi, out var d) ? d : 420;
        FastBoot = (Selected.Config.GetValueOrDefault("fastboot.forceFastBoot") ?? "yes")
            .Equals("yes", StringComparison.OrdinalIgnoreCase);
        ColdBootAlways = (Selected.Config.GetValueOrDefault("fastboot.forceColdBoot") ?? "no")
            .Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseSizeGb(string? s)
    {
        if (s is null) return null;
        s = s.Trim();
        if (s.EndsWith("G", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[..^1], out var g)) return g;
        if (s.EndsWith("M", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[..^1], out var m)) return m / 1024;
        if (s.EndsWith("K", StringComparison.OrdinalIgnoreCase) && long.TryParse(s[..^1], out var k)) return (int)(k / 1024 / 1024);
        // Some AVDs persist this as raw bytes (e.g. "8589934592"). Without this branch
        // the slider would snap back to the default 16 GB and the next "Save config"
        // would silently shrink the partition.
        if (long.TryParse(s, out var bytes) && bytes > 0) return (int)(bytes / 1024 / 1024 / 1024);
        return null;
    }

    [RelayCommand]
    private void Apply()
    {
        if (Selected is null) return;
        var updates = new Dictionary<string, string>
        {
            ["hw.ramSize"] = RamMb.ToString(),
            ["hw.cpu.ncore"] = Cores.ToString(),
            ["hw.lcd.width"] = ScreenW.ToString(),
            ["hw.lcd.height"] = ScreenH.ToString(),
            ["hw.lcd.density"] = Dpi.ToString(),
            ["fastboot.forceFastBoot"] = FastBoot ? "yes" : "no",
            ["fastboot.forceColdBoot"] = ColdBootAlways ? "yes" : "no",
        };
        _cfg.UpdateConfig(Selected, updates);
        _log.Success($"Applied config to '{Selected.Name}'. Some changes need a relaunch.");
    }

    [RelayCommand]
    private async Task ResizeDiskAsync(string mode)
    {
        if (Selected is null) return;
        bool wipe = mode == "wipe";
        IsBusy = true;
        try
        {
            var size = $"{DiskGb}G";
            var ok = await _cfg.ResizeDiskAsync(Selected, size, wipe, default);
            if (ok) _log.Success(wipe ? "Disk resized + data wiped. Next launch creates fresh partition." : "Disk size flag updated.");
            Refresh();
        }
        finally { IsBusy = false; }
    }
}
