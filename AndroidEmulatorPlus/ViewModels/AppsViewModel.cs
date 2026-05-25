using System.Collections.ObjectModel;
using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class AppsViewModel : ObservableObject
{
    private readonly AppService _apps;
    private readonly AdbService _adb;
    private readonly DeviceMonitor _monitor;
    private readonly LogService _log;
    private readonly PresetService _presets;

    public ObservableCollection<AndroidApp> Apps { get; } = new();
    public ObservableCollection<BloatPreset> BloatPresets { get; } = new();
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _includeSystem;
    [ObservableProperty] private bool _includeDisabled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _uninstallMode = "user"; // "user" (adb uninstall), "user0" (pm uninstall --user 0)

    public AppsViewModel(AppService apps, AdbService adb, DeviceMonitor monitor, LogService log, PresetService presets)
    {
        _apps = apps;
        _adb = adb;
        _monitor = monitor;
        _log = log;
        _presets = presets;
        foreach (var p in _presets.Presets) BloatPresets.Add(p);
    }

    /// <summary>Applies a preset by id (used by ItemsControl button bindings).</summary>
    [RelayCommand]
    private void ApplyPreset(BloatPreset? preset)
    {
        if (preset is null) return;
        var lookup = new HashSet<string>(preset.Packages, StringComparer.Ordinal);
        foreach (var a in Apps) a.IsSelected = lookup.Contains(a.Package);
        _log.Info($"Preset '{preset.Name}': selected {Apps.Count(a => a.IsSelected)} package(s).");
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is null) { _log.Warning("No emulator running."); return; }
        IsBusy = true;
        try
        {
            Apps.Clear();
            foreach (var a in await _apps.ListAsync(emu.Serial, includeSystem: IncludeSystem, includeDisabled: IncludeDisabled))
                Apps.Add(a);
        }
        finally { IsBusy = false; }
    }

    partial void OnIncludeSystemChanged(bool value) => _ = RefreshAsync();
    partial void OnIncludeDisabledChanged(bool value) => _ = RefreshAsync();

    [RelayCommand] private void SetUninstallModeUser() => UninstallMode = "user";
    [RelayCommand] private void SetUninstallModeUser0() => UninstallMode = "user0";

    public IEnumerable<AndroidApp> FilteredApps => string.IsNullOrWhiteSpace(Filter)
        ? Apps
        : Apps.Where(a => a.Package.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void SelectAll()  { foreach (var a in FilteredApps) a.IsSelected = true; }

    [RelayCommand]
    private void SelectNone() { foreach (var a in Apps) a.IsSelected = false; }

    [RelayCommand]
    private async Task UninstallSelectedAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is null) return;
        var sel = Apps.Where(a => a.IsSelected).ToList();
        if (sel.Count == 0) { _log.Warning("Select at least one app."); return; }
        var user0 = UninstallMode == "user0";
        IsBusy = true;
        int ok = 0, fail = 0;
        try
        {
            foreach (var a in sel)
            {
                var r = user0
                    ? await _apps.UninstallForUser0Async(emu.Serial, a.Package)
                    : await _apps.UninstallAsync(emu.Serial, a.Package);
                if (r.Combined.Contains("Success")) ok++;
                else fail++;
            }
            _log.Success($"{(user0 ? "Disabled for user 0" : "Uninstalled")}: {ok} ok, {fail} fail.");
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ReinstallSelectedAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is null) return;
        var sel = Apps.Where(a => a.IsSelected).ToList();
        if (sel.Count == 0) { _log.Warning("Select at least one app."); return; }
        IsBusy = true;
        int ok = 0, fail = 0;
        try
        {
            foreach (var a in sel)
            {
                var r = await _apps.ReinstallExistingAsync(emu.Serial, a.Package);
                if (r.Combined.Contains("installed for user") || r.Combined.Contains("Success")) ok++;
                else fail++;
            }
            _log.Success($"Re-enabled for user 0: {ok} ok, {fail} fail.");
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// B-05: populate the SizeText column for visible rows via <c>du -sb /data/data/&lt;pkg&gt;</c>.
    /// Root required; non-rooted emulators get a warning + skip.
    /// </summary>
    [RelayCommand]
    private async Task ComputeSizesAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is null) { _log.Warning("No emulator running."); return; }
        if (!await _adb.IsRootedAsync(emu.Serial))
        {
            _log.Warning("Computing per-app data size requires root on the emulator.");
            return;
        }
        IsBusy = true;
        try
        {
            int done = 0;
            foreach (var a in FilteredApps.ToList())
            {
                a.DataSizeBytes = await _adb.DataSizeAsync(emu.Serial, a.Package);
                done++;
            }
            _log.Info($"Computed sizes for {done} app(s).");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallApkAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Android packages (*.apk;*.apks;*.xapk;*.apkm)|*.apk;*.apks;*.xapk;*.apkm",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        await InstallApkFilesAsync(dlg.FileNames);
    }

    public async Task InstallApkFilesAsync(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is null) { _log.Warning("No emulator running."); return; }
        IsBusy = true;
        int ok = 0, fail = 0;
        try
        {
            foreach (var f in files)
            {
                var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                _log.Info($"Installing {System.IO.Path.GetFileName(f)}…");
                bool success;
                if (ext is ".apks" or ".xapk" or ".apkm")
                {
                    success = await InstallBundleAsync(emu.Serial, f);
                }
                else
                {
                    var r = await _apps.InstallApkAsync(emu.Serial, f);
                    success = r.Combined.Contains("Success");
                    if (!success) _log.Error("install failed: " + r.Combined.Trim());
                }
                if (success) { _log.Success("ok"); ok++; }
                else fail++;
            }
            if (files.Count > 1) _log.Success($"Batch install: {ok} ok, {fail} fail.");
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Extracts a .apks / .xapk / .apkm bundle and installs the inner splits via
    /// <c>install-multiple</c>; any OBB files are pushed to
    /// <c>/sdcard/Android/obb/&lt;pkg&gt;/</c>. Cleans up the extracted folder on exit.
    /// </summary>
    private async Task<bool> InstallBundleAsync(string serial, string bundlePath)
    {
        Services.AppService.BundleExtractResult? extracted = null;
        try
        {
            try { extracted = _apps.ExtractBundle(bundlePath); }
            catch (Exception ex) { _log.Error(ex.Message); return false; }

            var inst = await _apps.InstallSplitApksAsync(serial, extracted.Apks);
            if (!inst.Combined.Contains("Success"))
            {
                _log.Error("bundle install failed: " + inst.Combined.Trim());
                return false;
            }

            if (extracted.ObbFiles.Count > 0)
            {
                if (string.IsNullOrEmpty(extracted.ObbPackage))
                {
                    _log.Warning($"{extracted.ObbFiles.Count} OBB(s) found but the bundle didn't declare a package name. Skipping OBB push.");
                }
                else
                {
                    int okObb = 0;
                    foreach (var obb in extracted.ObbFiles)
                    {
                        if (await _apps.PushObbAsync(serial, extracted.ObbPackage, obb)) okObb++;
                    }
                    _log.Info($"OBB push: {okObb}/{extracted.ObbFiles.Count} ok.");
                }
            }
            return true;
        }
        finally
        {
            if (extracted is not null)
            {
                try { System.IO.Directory.Delete(extracted.WorkDir, true); } catch { }
            }
        }
    }

    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(FilteredApps));
}
