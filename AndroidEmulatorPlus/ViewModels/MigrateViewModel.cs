using System.Collections.ObjectModel;
using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class MigrateViewModel : ObservableObject
{
    private readonly AdbService _adb;
    private readonly MigrationService _mig;
    private readonly DeviceMonitor _monitor;
    private readonly LogService _log;

    public ObservableCollection<AndroidApp> Packages { get; } = new();
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _doApk = true;
    [ObservableProperty] private bool _doInternal = true;
    [ObservableProperty] private bool _doExternal = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _stepText = "";
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _phoneStatus = "no phone";
    [ObservableProperty] private string _emuStatus = "no emulator";
    [ObservableProperty] private string _summary = "";

    public MigrateViewModel(AdbService adb, MigrationService mig, DeviceMonitor monitor, LogService log)
    {
        _adb = adb;
        _mig = mig;
        _monitor = monitor;
        _log = log;
        _monitor.Changed += devs => { _ = RefreshAsync(); };
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var phone = _monitor.Current.FirstOrDefault(d => !d.IsEmulator);
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);

        if (phone is null) PhoneStatus = "no phone connected";
        else
        {
            var rooted = await _adb.IsRootedAsync(phone.Serial);
            PhoneStatus = $"{phone.Display}{(rooted ? "  ✓ rooted" : "  ⚠ NOT rooted — data copy unavailable")}";
        }

        if (emu is null) EmuStatus = "no emulator running";
        else
        {
            var rooted = await _adb.IsRootedAsync(emu.Serial);
            EmuStatus = $"{emu.Serial}{(rooted ? "  ✓ rooted" : "  ⚠ NOT rooted — data restore unavailable")}";
        }

        if (phone is null) return;
        Packages.Clear();
        foreach (var p in await _adb.ListPackagesAsync(phone.Serial))
            Packages.Add(new AndroidApp { Package = p, IsSelected = true });
    }

    public IEnumerable<AndroidApp> FilteredPackages => string.IsNullOrWhiteSpace(Filter)
        ? Packages
        : Packages.Where(p => p.Package.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var p in Packages) p.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var p in Packages) p.IsSelected = false;
    }

    [RelayCommand]
    private async Task MigrateAsync()
    {
        var phone = _monitor.Current.FirstOrDefault(d => !d.IsEmulator);
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (phone is null || emu is null) { _log.Warning("Need both a phone and an emulator connected."); return; }

        var sel = Packages.Where(p => p.IsSelected).ToList();
        if (sel.Count == 0) { _log.Warning("Select at least one package."); return; }

        IsBusy = true;
        int ok = 0, fail = 0; long totalBytes = 0;
        try
        {
            int idx = 0;
            foreach (var p in sel)
            {
                idx++;
                StepText = $"{idx}/{sel.Count}  {p.Package}";
                ProgressFraction = (double)idx / sel.Count;

                if (DoApk)
                {
                    var r = await _mig.TransferApkAsync(phone.Serial, emu.Serial, p.Package, default);
                    if (r.Success) _log.Info($"APK ok {p.Package} ({r.Detail})");
                    else { _log.Warning($"APK fail {p.Package}: {r.Detail}"); fail++; continue; }
                }

                if (DoInternal)
                {
                    var r = await _mig.TransferInternalDataAsync(phone.Serial, emu.Serial, p.Package, default);
                    totalBytes += r.SizeBytes;
                    if (r.Success) _log.Info($"data ok {p.Package} ({r.SizeBytes / 1024 / 1024} MB, {r.Detail})");
                    else _log.Warning($"data fail {p.Package}: {r.Detail}");
                }

                if (DoExternal)
                {
                    var r = await _mig.TransferExternalDataAsync(phone.Serial, emu.Serial, p.Package, default);
                    totalBytes += r.SizeBytes;
                    if (r.Success && r.SizeBytes > 0) _log.Info($"ext ok {p.Package} ({r.SizeBytes / 1024 / 1024} MB)");
                }
                ok++;
            }
            Summary = $"{ok} ok, {fail} fail, {totalBytes / 1024 / 1024} MB data";
            _log.Success("Migration finished. " + Summary);
        }
        finally
        {
            IsBusy = false;
            StepText = "";
            ProgressFraction = 0;
        }
    }

    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(FilteredPackages));
}
