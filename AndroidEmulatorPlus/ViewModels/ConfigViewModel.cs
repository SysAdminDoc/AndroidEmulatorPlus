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
    [ObservableProperty] private string _gpuMode = "host";
    [ObservableProperty] private ScreenPreset? _screenPreset;

    /// <summary>Choices for the GPU mode picker (A-22).</summary>
    public IReadOnlyList<string> GpuModes { get; } = new[]
    {
        "host", "swiftshader_indirect", "angle_indirect", "guest", "off",
    };

    /// <summary>Choices for the screen preset picker (A-21).</summary>
    public IReadOnlyList<ScreenPreset> ScreenPresets { get; } = ScreenPreset.All;

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
        var gpu = Selected.Config.GetValueOrDefault("hw.gpu.mode");
        GpuMode = string.IsNullOrEmpty(gpu) ? "host" : gpu;
        // Match the current W/H/DPI against a known preset for the picker; null if custom.
        ScreenPreset = ScreenPresets.FirstOrDefault(p => p.Matches(ScreenW, ScreenH, Dpi));
    }

    private void NormalizeHardwareInputs()
    {
        RamMb = Clamp(RamMb, 512, 65536);
        Cores = Clamp(Cores, 1, 64);
        DiskGb = Clamp(DiskGb, 1, 4096);
        ScreenW = Clamp(ScreenW, 240, 8192);
        ScreenH = Clamp(ScreenH, 240, 8192);
        Dpi = Clamp(Dpi, 80, 1000);
        if (!GpuModes.Contains(GpuMode)) GpuMode = "host";
    }

    private static int Clamp(int value, int min, int max)
        => Math.Min(Math.Max(value, min), max);

    partial void OnScreenPresetChanged(ScreenPreset? value)
    {
        if (value is null) return;
        ScreenW = value.Width;
        ScreenH = value.Height;
        Dpi = value.Dpi;
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
        NormalizeHardwareInputs();
        var updates = new Dictionary<string, string>
        {
            ["hw.ramSize"] = RamMb.ToString(),
            ["hw.cpu.ncore"] = Cores.ToString(),
            ["hw.lcd.width"] = ScreenW.ToString(),
            ["hw.lcd.height"] = ScreenH.ToString(),
            ["hw.lcd.density"] = Dpi.ToString(),
            ["fastboot.forceFastBoot"] = FastBoot ? "yes" : "no",
            ["fastboot.forceColdBoot"] = ColdBootAlways ? "yes" : "no",
            ["hw.gpu.mode"] = string.IsNullOrEmpty(GpuMode) ? "host" : GpuMode,
            ["hw.gpu.enabled"] = "yes",
        };
        _cfg.UpdateConfig(Selected, updates);
        _log.Success($"Applied config to '{Selected.Name}'. Some changes need a relaunch.");
    }

    [RelayCommand]
    private async Task ResizeDiskAsync(string mode)
    {
        if (Selected is null) return;
        NormalizeHardwareInputs();
        bool wipe = mode == "wipe";
        if (wipe)
        {
            var (snaps, overlays) = ConfigService.PreviewWipe(Selected);
            var detail = new System.Text.StringBuilder();
            if (overlays.Count > 0)
            {
                detail.AppendLine("qcow2 overlays to delete:");
                foreach (var o in overlays) detail.AppendLine($"  • {o}");
            }
            if (snaps.Count > 0)
            {
                if (detail.Length > 0) detail.AppendLine();
                detail.AppendLine($"Snapshots to destroy ({snaps.Count}):");
                foreach (var s in snaps) detail.AppendLine($"  • {s}");
            }
            if (detail.Length == 0) detail.AppendLine("(this AVD has never booted — nothing to wipe yet)");

            var ok = Views.ConfirmDialog.Show(
                owner: null,
                header: $"Wipe data on '{Selected.Name}'?",
                body: $"The qcow2 overlays for AVD '{Selected.Name}' will be deleted so the partition recreates at {DiskGb} GB on next launch. " +
                      "Any saved snapshots inside this AVD will be permanently lost. " +
                      "Type WIPE to confirm.",
                detail: detail.ToString().TrimEnd(),
                confirmButtonText: "Wipe data",
                typedConfirm: "WIPE");
            if (!ok) { _log.Info("Wipe cancelled."); return; }
        }

        IsBusy = true;
        try
        {
            var size = $"{DiskGb}G";
            var ok2 = await _cfg.ResizeDiskAsync(Selected, size, wipe, default);
            if (ok2) _log.Success(wipe ? "Disk resized + data wiped. Next launch creates fresh partition." : "Disk size flag updated.");
            Refresh();
        }
        finally { IsBusy = false; }
    }
}
