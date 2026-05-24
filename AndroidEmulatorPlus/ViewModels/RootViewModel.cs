using System.Collections.ObjectModel;
using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class RootViewModel : ObservableObject
{
    private readonly RootService _root;
    private readonly AdbService _adb;
    private readonly AvdService _avds;
    private readonly LogService _log;
    private readonly DeviceMonitor _monitor;
    private readonly SdkLocator _sdk;

    public ObservableCollection<Avd> Avds { get; } = new();
    [ObservableProperty] private Avd? _selectedAvd;
    [ObservableProperty] private string _status = "—";
    [ObservableProperty] private string _step = "";
    [ObservableProperty] private bool _isBusy;

    public RootViewModel(RootService root, AdbService adb, AvdService avds, LogService log,
        DeviceMonitor monitor, SdkLocator sdk)
    {
        _root = root;
        _adb = adb;
        _avds = avds;
        _log = log;
        _monitor = monitor;
        _sdk = sdk;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Avds.Clear();
        foreach (var a in _avds.List()) Avds.Add(a);
        SelectedAvd ??= Avds.FirstOrDefault();
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is not null)
        {
            var rooted = await _adb.IsRootedAsync(emu.Serial);
            Status = rooted ? "Rooted (Magisk daemon active)" : "Not rooted (or root not granted to shell)";
        }
        else Status = "No emulator running.";
    }

    [RelayCommand]
    private async Task RootAsync()
    {
        if (SelectedAvd is null) { _log.Warning("Pick an AVD first."); return; }
        var progress = new Progress<string>(s => { Step = s; _log.Info(s); });
        IsBusy = true;
        try
        {
            await _root.EnsureRootAvdAsync(progress, default);
            var tag = await _root.DownloadLatestMagiskAsync(progress, default);
            _log.Success($"Magisk {tag} cached.");

            var ramdisk = _root.FindRamdiskFor(SelectedAvd.Name)
                ?? throw new InvalidOperationException("Ramdisk for this AVD's system image was not found.");
            var rel = _root.RelativeRamdiskPath(ramdisk)!;

            // Back up original ramdisk if there isn't one yet.
            var orig = ramdisk + ".original";
            if (!System.IO.File.Exists(orig)) System.IO.File.Copy(ramdisk, orig);

            // The patch step must run with a booted emulator visible via adb.
            var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
            if (emu is null)
            {
                _log.Warning("Launch the AVD first so rootAVD can patch its ramdisk via adb. Aborting.");
                return;
            }

            var ok = await _root.PatchAsync(rel, progress, l => _log.Detail(l), default);
            if (!ok) { _log.Error("rootAVD reported failure. Restore stock ramdisk via Un-Root if the emulator hangs."); return; }
            _log.Success("Ramdisk patched. Cold-boot the emulator now (Avd tab → Cold-Boot).");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
            Step = "";
        }
    }

    [RelayCommand]
    private async Task VerifyAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (emu is null) { _log.Warning("No emulator running."); return; }
        IsBusy = true;
        try
        {
            var ok = await _root.VerifyRootAsync(emu.Serial, default);
            if (ok)
            {
                await _root.PersistShellPolicyAsync(emu.Serial, default);
                _log.Success("Root verified. Magisk shell policy persisted.");
            }
            else
            {
                _log.Warning("Root not granted to shell yet. Open Magisk app on the emulator and tap GRANT on the next prompt.");
            }
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void UnRoot()
    {
        if (SelectedAvd is null) return;
        var ramdisk = _root.FindRamdiskFor(SelectedAvd.Name);
        if (ramdisk is null) { _log.Warning("Ramdisk not found."); return; }
        try
        {
            _root.RestoreRamdisk(ramdisk);
            _log.Success("Restored stock ramdisk. Cold-boot the emulator to unroot.");
        }
        catch (Exception ex) { _log.Error(ex.Message); }
    }
}
