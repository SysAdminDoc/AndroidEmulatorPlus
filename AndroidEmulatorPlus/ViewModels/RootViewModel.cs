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
    private readonly EmulatorService _emu;

    public ObservableCollection<Avd> Avds { get; } = new();
    [ObservableProperty] private Avd? _selectedAvd;
    [ObservableProperty] private string _status = "—";
    [ObservableProperty] private string _step = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _needsLaunch;

    public RootViewModel(RootService root, AdbService adb, AvdService avds, LogService log,
        DeviceMonitor monitor, SdkLocator sdk, EmulatorService emu)
    {
        _root = root;
        _adb = adb;
        _avds = avds;
        _log = log;
        _monitor = monitor;
        _sdk = sdk;
        _emu = emu;
        _monitor.Changed += _ => UpdateNeedsLaunch();
    }

    private void UpdateNeedsLaunch()
    {
        NeedsLaunch = SelectedAvd is not null
            && !_monitor.Current.Any(d => d.IsEmulator && d.IsOnline);
    }

    partial void OnSelectedAvdChanged(Avd? value) => UpdateNeedsLaunch();

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
        UpdateNeedsLaunch();
    }

    /// <summary>
    /// A-28: inline "Launch &lt;name&gt;" CTA. Launches the selected AVD, waits for adb to
    /// report it, and then re-enters the Root flow once boot completes.
    /// </summary>
    [RelayCommand]
    private async Task LaunchAndRootAsync()
    {
        if (SelectedAvd is null) { _log.Warning("Pick an AVD first."); return; }
        _log.Info($"Launching '{SelectedAvd.Name}' before rooting…");
        IsBusy = true;
        Step = $"Launching {SelectedAvd.Name}…";
        try
        {
            _emu.Launch(SelectedAvd.Name);
            // Poll the device monitor for up to two minutes.
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            Models.Device? emu = null;
            while (DateTime.UtcNow < deadline)
            {
                emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
                if (emu is not null) break;
                await Task.Delay(2000);
            }
            if (emu is null) { _log.Error("Emulator did not appear in adb within 2 minutes."); return; }
            Step = "Waiting for boot…";
            if (!await _adb.WaitForBootAsync(emu.Serial, TimeSpan.FromMinutes(3)))
            {
                _log.Error("sys.boot_completed never became 1. Aborting root.");
                return;
            }
            _log.Success("Emulator booted. Starting root flow…");
        }
        finally
        {
            IsBusy = false;
            Step = "";
        }
        await RootAsync();
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
        var backup = _root.FindBackupRamdisk(ramdisk);
        if (backup is null)
        {
            _log.Warning("No stock-ramdisk backup found (.original / .backup). Aborting Un-Root to avoid leaving the AVD unbootable.");
            return;
        }
        var ok = Views.ConfirmDialog.Show(
            owner: null,
            header: $"Un-root the system image for '{SelectedAvd.Name}'?",
            body: "This overwrites the patched ramdisk with the backup that was saved before the last Magisk patch. All AVDs that share this system image will lose root; cold-boot them after to apply.",
            detail: $"Ramdisk: {ramdisk}\nBackup:  {backup}",
            confirmButtonText: "Restore stock ramdisk");
        if (!ok) return;
        try
        {
            _root.RestoreRamdisk(ramdisk);
            _log.Success("Restored stock ramdisk. Cold-boot the emulator to unroot.");
        }
        catch (Exception ex) { _log.Error(ex.Message); }
    }
}
