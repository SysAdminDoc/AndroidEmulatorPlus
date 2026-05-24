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

    public ObservableCollection<AndroidApp> Apps { get; } = new();
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _includeSystem;
    [ObservableProperty] private bool _isBusy;

    public AppsViewModel(AppService apps, AdbService adb, DeviceMonitor monitor, LogService log)
    {
        _apps = apps;
        _adb = adb;
        _monitor = monitor;
        _log = log;
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
            foreach (var a in await _apps.ListAsync(emu.Serial, userOnly: !IncludeSystem))
                Apps.Add(a);
        }
        finally { IsBusy = false; }
    }

    public IEnumerable<AndroidApp> FilteredApps => string.IsNullOrWhiteSpace(Filter)
        ? Apps
        : Apps.Where(a => a.Package.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void SelectAll()  { foreach (var a in FilteredApps) a.IsSelected = true; }

    [RelayCommand]
    private void SelectNone() { foreach (var a in Apps) a.IsSelected = false; }

    [RelayCommand]
    private void SelectGoogleBloat()
    {
        foreach (var a in Apps) a.IsSelected = AppService.BloatPresetGoogle.Contains(a.Package);
    }

    [RelayCommand]
    private void SelectSamsungBloat()
    {
        foreach (var a in Apps) a.IsSelected = AppService.BloatPresetSamsung.Contains(a.Package);
    }

    [RelayCommand]
    private async Task UninstallSelectedAsync()
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
                var r = await _apps.UninstallAsync(emu.Serial, a.Package);
                if (r.Combined.Contains("Success")) ok++;
                else fail++;
            }
            _log.Success($"Uninstalled: {ok} ok, {fail} fail.");
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallApkAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is null) { _log.Warning("No emulator running."); return; }
        var dlg = new OpenFileDialog
        {
            Filter = "Android packages (*.apk;*.apks;*.xapk)|*.apk;*.apks;*.xapk",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            foreach (var f in dlg.FileNames)
            {
                _log.Info($"Installing {System.IO.Path.GetFileName(f)}…");
                var r = await _apps.InstallApkAsync(emu.Serial, f);
                if (r.Combined.Contains("Success")) _log.Success("ok");
                else _log.Error("install failed: " + r.Combined.Trim());
            }
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(FilteredApps));
}
