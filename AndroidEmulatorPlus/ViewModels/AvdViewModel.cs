using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class AvdViewModel : ObservableObject
{
    private readonly AvdService _avds;
    private readonly EmulatorService _emu;
    private readonly AdbService _adb;
    private readonly DeviceMonitor _monitor;
    private readonly LogService _log;
    private readonly SdkLocator _sdk;

    public ObservableCollection<Avd> Avds { get; } = new();
    [ObservableProperty] private Avd? _selected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasAvds;

    // Create form
    [ObservableProperty] private string _newName = "MyEmulator";
    [ObservableProperty] private string _newDevice = "pixel_7";
    [ObservableProperty] private string? _newImage;
    public ObservableCollection<string> AvailableImages { get; } = new();
    public ObservableCollection<string> AvailableDevices { get; } = new()
    {
        "pixel_7", "pixel_7_pro", "pixel_8", "pixel_8_pro", "pixel_fold", "pixel_tablet",
        "Nexus 6P", "Nexus 5X", "small_phone",
    };

    public AvdViewModel(AvdService avds, EmulatorService emu, AdbService adb,
        DeviceMonitor monitor, LogService log, SdkLocator sdk)
    {
        _avds = avds;
        _emu = emu;
        _adb = adb;
        _monitor = monitor;
        _log = log;
        _sdk = sdk;
        _monitor.Changed += _ => _ = RefreshRunningStateAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Avds.Clear();
            foreach (var a in _avds.List()) Avds.Add(a);
            HasAvds = Avds.Count > 0;
            AvailableImages.Clear();
            // Order by parsed API level descending, then by variant (playstore > google_apis > default).
            // Alphabetical order put android-9 before android-25 and defaulted Create AVD to API 9.
            foreach (var img in (await _avds.ListSystemImagesAsync()).OrderByDescending(SystemImageSortKey))
                AvailableImages.Add(img);
            NewImage ??= AvailableImages.FirstOrDefault();
            await RefreshRunningStateAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Sort key for `system-images;android-NN;variant;abi` strings. Higher = pick first.
    /// </summary>
    private static (int Api, int VariantRank, string Raw) SystemImageSortKey(string img)
    {
        var parts = img.Split(';');
        int api = 0;
        if (parts.Length > 1)
        {
            var m = System.Text.RegularExpressions.Regex.Match(parts[1], @"\d+");
            if (m.Success) int.TryParse(m.Value, out api);
        }
        int rank = 0;
        if (parts.Length > 2)
        {
            var v = parts[2].ToLowerInvariant();
            if (v.Contains("playstore")) rank = 3;
            else if (v.Contains("google_apis")) rank = 2;
            else if (v.Contains("default")) rank = 1;
        }
        return (api, rank, img);
    }

    /// <summary>For each currently-attached emulator, resolve which AVD it is and tag the row.</summary>
    private async Task RefreshRunningStateAsync()
    {
        try
        {
            var emus = _monitor.Current.Where(d => d.IsEmulator && d.IsOnline).ToList();
            var serialByAvd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in emus)
            {
                var name = await _adb.AvdNameForSerialAsync(d.Serial);
                if (!string.IsNullOrEmpty(name)) serialByAvd[name] = d.Serial;
            }
            foreach (var a in Avds)
                a.RunningSerial = serialByAvd.TryGetValue(a.Name, out var s) ? s : null;
        }
        catch { /* best-effort */ }
    }

    [RelayCommand]
    private void Launch(Avd? avd)
    {
        if (avd is null) return;
        _emu.Launch(avd.Name);
    }

    [RelayCommand]
    private void LaunchCold(Avd? avd)
    {
        if (avd is null) return;
        _emu.Launch(avd.Name, coldBoot: true);
    }

    [RelayCommand]
    private async Task DeleteAsync(Avd? avd)
    {
        if (avd is null) return;
        // Build the confirmation detail with folder + best-effort size of the .avd dir.
        var folder = _avds.FolderFor(avd.Name);
        var size = "";
        if (folder is not null)
        {
            try
            {
                long bytes = 0;
                foreach (var f in System.IO.Directory.EnumerateFiles(folder, "*", System.IO.SearchOption.AllDirectories))
                    try { bytes += new System.IO.FileInfo(f).Length; } catch { }
                size = bytes switch
                {
                    < 1024L * 1024 => $"{bytes / 1024} KB",
                    < 1024L * 1024 * 1024 => $"{bytes / 1024 / 1024} MB",
                    _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB",
                };
            }
            catch { }
        }
        var detail = folder is null
            ? null
            : $"Folder: {folder}{(string.IsNullOrEmpty(size) ? "" : $"\nSize:   {size}")}";
        var ok = Views.ConfirmDialog.Show(
            owner: null,
            header: $"Delete AVD '{avd.Name}'?",
            body: "This calls `avdmanager delete avd` and removes the AVD folder. All data inside the emulator (apps, accounts, snapshots) will be lost.",
            detail: detail,
            confirmButtonText: "Delete AVD");
        if (!ok) { _log.Info("Delete cancelled."); return; }

        IsBusy = true;
        try
        {
            var r = await _avds.DeleteAsync(avd.Name);
            if (r.Success) _log.Success($"Deleted AVD '{avd.Name}'.");
            else _log.Error("Delete failed: " + r.Combined.Trim());
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task StopAsync(Avd? avd)
    {
        if (avd?.RunningSerial is null) { _log.Warning("That AVD is not currently running."); return; }
        var serial = avd.RunningSerial;
        _log.Info($"Stopping emulator {serial} (AVD '{avd.Name}')…");
        var r = await _adb.EmuKillAsync(serial);
        if (r.Success) _log.Success($"Stopped {serial}.");
        else _log.Warning("Stop returned non-zero: " + r.Combined.Trim());
        await RefreshRunningStateAsync();
    }

    [RelayCommand]
    private void ShowOnDisk(Avd? avd)
    {
        if (avd is null) return;
        var dir = _avds.FolderFor(avd.Name);
        if (dir is null) { _log.Warning("AVD folder not found on disk."); return; }
        try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); }
        catch (Exception ex) { _log.Error("Open folder failed: " + ex.Message); }
    }

    [RelayCommand]
    private void CreateShortcut(Avd? avd)
    {
        if (avd is null) return;
        try { _avds.CreateDesktopShortcut(avd.Name); }
        catch (Exception ex) { _log.Error("Shortcut create failed: " + ex.Message); }
    }

    [RelayCommand]
    private async Task RenameAsync(Avd? avd)
    {
        if (avd is null) return;
        if (avd.IsRunning)
        {
            _log.Warning("Stop the emulator before renaming its AVD.");
            return;
        }
        var existing = new HashSet<string>(Avds.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        var newName = Views.PromptDialog.Show(
            owner: null,
            header: $"Rename '{avd.Name}'",
            body: "AVD names may contain letters, digits, '.', '_' and '-'.",
            initial: avd.Name,
            okText: "Rename",
            validate: text =>
            {
                var t = (text ?? "").Trim();
                if (string.IsNullOrEmpty(t)) return "Name cannot be empty.";
                if (t == avd.Name) return "Pick a different name.";
                if (!System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z0-9._-]+$"))
                    return "Only letters, digits, '.', '_' and '-' are allowed.";
                if (existing.Contains(t)) return $"An AVD named '{t}' already exists.";
                return null;
            });
        if (newName is null) return;
        newName = newName.Trim();
        IsBusy = true;
        try
        {
            var r = await _avds.RenameAsync(avd.Name, newName);
            if (r.Success) _log.Success($"Renamed '{avd.Name}' → '{newName}'.");
            else _log.Error("avdmanager move: " + r.Combined.Trim());
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewImage))
        {
            _log.Warning("Pick a name and a system image first.");
            return;
        }
        IsBusy = true;
        try
        {
            var r = await _avds.CreateAsync(NewName.Trim(), NewImage!, NewDevice);
            if (r.Success) _log.Success($"Created AVD '{NewName}'.");
            else _log.Error("avdmanager: " + r.Combined.Trim());
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }
}
