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
    private readonly CacheDiagnosticsService _cache;
    private readonly BundleInstallerService _bundleInstaller;

    public ObservableCollection<AndroidApp> Apps { get; } = new();
    public ObservableCollection<BloatPreset> BloatPresets { get; } = new();
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _includeSystem;
    [ObservableProperty] private bool _includeDisabled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _uninstallMode = "user"; // "user" (adb uninstall), "user0" (pm uninstall --user 0)

    public AppsViewModel(AppService apps, AdbService adb, DeviceMonitor monitor, LogService log, PresetService presets, CacheDiagnosticsService cache, BundleInstallerService bundleInstaller)
    {
        _apps = apps;
        _adb = adb;
        _monitor = monitor;
        _log = log;
        _presets = presets;
        _cache = cache;
        _bundleInstaller = bundleInstaller;
        foreach (var p in _presets.Presets) BloatPresets.Add(p);
    }

    [ObservableProperty] private bool _verifySignaturesBeforeInstall = true;
    private CancellationTokenSource? _refreshCts;
    private int _refreshGeneration;

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
        if (emu is null)
        {
            Apps.Clear();
            NotifyListStateChanged();
            _log.Warning("No emulator running.");
            return;
        }
        var generation = Interlocked.Increment(ref _refreshGeneration);
        _refreshCts?.Cancel();
        var refreshCts = new CancellationTokenSource();
        _refreshCts = refreshCts;
        var ct = refreshCts.Token;
        IsBusy = true;
        try
        {
            var list = await _apps.ListAsync(emu.Serial, includeSystem: IncludeSystem, includeDisabled: IncludeDisabled, ct: ct);
            if (generation != _refreshGeneration) return;
            Apps.Clear();
            foreach (var a in list)
                Apps.Add(a);
            NotifyListStateChanged();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (generation == _refreshGeneration)
            {
                IsBusy = false;
                _refreshCts?.Dispose();
                _refreshCts = null;
            }
            else
            {
                refreshCts.Dispose();
            }
        }
    }

    partial void OnIncludeSystemChanged(bool value) => _ = RefreshAsync();
    partial void OnIncludeDisabledChanged(bool value) => _ = RefreshAsync();

    [RelayCommand] private void SetUninstallModeUser() => UninstallMode = "user";
    [RelayCommand] private void SetUninstallModeUser0() => UninstallMode = "user0";

    public IEnumerable<AndroidApp> FilteredApps => string.IsNullOrWhiteSpace(Filter)
        ? Apps
        : Apps.Where(a => a.Package.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    public bool HasApps => Apps.Count > 0;
    public bool HasFilteredApps => FilteredApps.Any();
    public bool IsFilteredEmpty => HasApps && !HasFilteredApps;

    private void NotifyListStateChanged()
    {
        OnPropertyChanged(nameof(FilteredApps));
        OnPropertyChanged(nameof(HasApps));
        OnPropertyChanged(nameof(HasFilteredApps));
        OnPropertyChanged(nameof(IsFilteredEmpty));
    }

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
            // C-13: iterate the full list, not just FilteredApps — otherwise rows
            // hidden by the filter stay at "—" after the filter is cleared.
            int done = 0;
            foreach (var a in Apps.ToList())
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
                _log.Info($"Installing {System.IO.Path.GetFileName(f)}…");
                var success = await _bundleInstaller.InstallFileAsync(
                    emu.Serial, f, VerifySignaturesBeforeInstall,
                    onSignerMismatch: OnSignerMismatchAsync);
                if (success) { _log.Success("ok"); ok++; } else fail++;
            }
            if (files.Count > 1) _log.Success($"Batch install: {ok} ok, {fail} fail.");
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// C-04: prompt the user when the APK signer differs from what's already
    /// installed on the device. Runs on the UI thread because it opens a
    /// modal ConfirmDialog.
    /// </summary>
    private Task<bool> OnSignerMismatchAsync(string pkg, string? detail)
    {
        var ok = Views.ConfirmDialog.Show(
            owner: null,
            header: $"Signer mismatch for '{pkg}'",
            body: "The signing certificate of this APK does NOT match the certificate of the version already installed on the device. " +
                  "Continuing will replace the installed app with one signed by a different developer — common for re-signed / patched APKs and a red flag for sideloads.",
            detail: detail,
            confirmButtonText: "Install anyway");
        if (!ok) _log.Warning($"Install of {pkg} skipped (signer mismatch).");
        return Task.FromResult(ok);
    }

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is null) { _log.Warning("No emulator running."); return; }
        var sel = Apps.Where(a => a.IsSelected).ToList();
        if (sel.Count == 0) { _log.Warning("Select at least one app."); return; }
        if (!await _adb.IsRootedAsync(emu.Serial)) { _log.Warning("Export requires root on the emulator."); return; }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "AEP app data export (*.zip)|*.zip|All files|*.*",
            FileName = sel.Count == 1 ? $"{sel[0].Package}.zip" : "app-data-export.zip",
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            if (sel.Count == 1)
            {
                await _apps.ExportAppDataAsync(emu.Serial, sel[0].Package, dlg.FileName);
            }
            else
            {
                var dir = System.IO.Path.GetDirectoryName(dlg.FileName)!;
                foreach (var a in sel)
                {
                    var p = System.IO.Path.Combine(dir, $"{a.Package}.zip");
                    await _apps.ExportAppDataAsync(emu.Serial, a.Package, p);
                }
            }
        }
        finally { IsBusy = false; _cache.NotifyChanged(); }
    }

    [RelayCommand]
    private async Task ImportZipAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is null) { _log.Warning("No emulator running."); return; }
        if (!await _adb.IsRootedAsync(emu.Serial)) { _log.Warning("Import requires root on the emulator."); return; }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "AEP app data export (*.zip)|*.zip|All files|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            foreach (var z in dlg.FileNames)
                await _apps.ImportAppDataAsync(emu.Serial, z);
            await RefreshAsync();
        }
        finally { IsBusy = false; _cache.NotifyChanged(); }
    }

    partial void OnFilterChanged(string value) => NotifyListStateChanged();
}
