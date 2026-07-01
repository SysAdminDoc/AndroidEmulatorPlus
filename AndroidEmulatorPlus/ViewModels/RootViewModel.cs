using System.Collections.ObjectModel;
using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

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
    private readonly MagiskService _magisk;
    private readonly CaCertService _caCert;
    private readonly FridaService _frida;
    private readonly ToastService _toast;

    public ObservableCollection<Avd> Avds { get; } = new();
    [ObservableProperty] private Avd? _selectedAvd;
    [ObservableProperty] private string _status = "—";
    [ObservableProperty] private string _step = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _needsLaunch;
    [ObservableProperty] private string _caCertStatus = "No CA certificate installed this session.";
    [ObservableProperty] private string _fridaStatus = "No emulator attached.";
    [ObservableProperty] private bool _isFridaRunning;
    [ObservableProperty] private bool _hasDownloadProgress;
    [ObservableProperty] private bool _isDownloadProgressIndeterminate = true;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadProgressText = "";

    private CancellationTokenSource? _cts;

    public RootViewModel(RootService root, AdbService adb, AvdService avds, LogService log,
        DeviceMonitor monitor, SdkLocator sdk, EmulatorService emu, MagiskService magisk,
        CaCertService caCert, FridaService frida, ToastService toast)
    {
        _root = root;
        _adb = adb;
        _avds = avds;
        _log = log;
        _monitor = monitor;
        _sdk = sdk;
        _emu = emu;
        _magisk = magisk;
        _caCert = caCert;
        _frida = frida;
        _toast = toast;
        _monitor.Changed += _ => UpdateNeedsLaunch();
    }

    /// <summary>C-07 / R-03: opens the Magisk module manager against the running emulator.</summary>
    [RelayCommand]
    private void OpenModules()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline);
        if (emu is null) { _log.Warning("Modules: no emulator attached."); return; }
        var dlg = new Views.MagiskModulesDialog(_magisk, emu.Serial)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        dlg.ShowDialog();
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
            await RefreshFridaStateAsync(emu.Serial);
        }
        else
        {
            Status = "No emulator running.";
            FridaStatus = "No emulator attached.";
            IsFridaRunning = false;
        }
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
        if (IsBusy) return;
        if (SelectedAvd is null) { _log.Warning("Pick an AVD first."); return; }
        var progress = new Progress<string>(s => { Step = s; _log.Info(s); });
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            await _root.EnsureRootAvdAsync(progress, ct);
            ResetDownloadProgress();
            var tag = await _root.DownloadLatestMagiskAsync(progress, ct,
                new Progress<(long received, long? total)>(ReportDownloadProgress));
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

            var ok = await _root.PatchAsync(rel, progress, l => _log.Detail(l), ct);
            if (!ok) { _log.Error("rootAVD reported failure. Restore stock ramdisk via Un-Root if the emulator hangs."); return; }
            _log.Success("Ramdisk patched. Cold-boot the emulator now (Avd tab → Cold-Boot).");
            _toast.Show("Root complete", "Ramdisk patched. Cold-boot the emulator to apply.");
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Root flow cancelled by user.");
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
            Step = "";
            HasDownloadProgress = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }

    [RelayCommand]
    private async Task InstallCaCertAsync()
    {
        if (IsBusy) return;
        var emu = CurrentOnlineEmulator();
        if (emu is null) { _log.Warning("CA cert install: no emulator attached."); return; }

        var dlg = new OpenFileDialog
        {
            Title = "Choose proxy CA certificate",
            Filter = "Certificate files (*.cer;*.crt;*.pem;*.der)|*.cer;*.crt;*.pem;*.der|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(s => { Step = s; CaCertStatus = s; _log.Info(s); });
        try
        {
            var ok = await _caCert.InstallCaCertAsync(emu.Serial, dlg.FileName, progress, _cts.Token);
            CaCertStatus = ok ? "Installed. Reboot the emulator to activate." : "Install failed.";
        }
        catch (OperationCanceledException)
        {
            CaCertStatus = "Install cancelled.";
            _log.Warning("CA cert install cancelled.");
        }
        catch (Exception ex)
        {
            CaCertStatus = "Install failed.";
            _log.Error("CA cert install failed: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
            Step = "";
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task DeployFridaAsync()
    {
        if (IsBusy) return;
        var emu = CurrentOnlineEmulator();
        if (emu is null) { _log.Warning("Frida deploy: no emulator attached."); return; }

        IsBusy = true;
        ResetDownloadProgress();
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(s => { Step = s; FridaStatus = s; _log.Info(s); });
        try
        {
            var ok = await _frida.DeployAsync(emu.Serial, progress,
                new Progress<(long received, long? total)>(ReportDownloadProgress), _cts.Token);
            IsFridaRunning = ok && await _frida.IsRunningAsync(emu.Serial);
            FridaStatus = IsFridaRunning ? "frida-server is running." : "frida-server deployed; start status uncertain.";
        }
        catch (OperationCanceledException)
        {
            FridaStatus = "Deploy cancelled.";
            _log.Warning("Frida deploy cancelled.");
        }
        catch (Exception ex)
        {
            FridaStatus = "Deploy failed.";
            _log.Error("Frida deploy failed: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
            Step = "";
            HasDownloadProgress = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task StopFridaAsync()
    {
        var emu = CurrentOnlineEmulator();
        if (emu is null) { _log.Warning("Frida stop: no emulator attached."); return; }
        IsBusy = true;
        try
        {
            await _frida.StopAsync(emu.Serial);
            IsFridaRunning = false;
            FridaStatus = "frida-server stopped.";
        }
        catch (Exception ex)
        {
            FridaStatus = "Stop failed.";
            _log.Error("Frida stop failed: " + ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshFridaStatusAsync()
    {
        var emu = CurrentOnlineEmulator();
        if (emu is null)
        {
            FridaStatus = "No emulator attached.";
            IsFridaRunning = false;
            return;
        }
        await RefreshFridaStateAsync(emu.Serial);
    }

    /// <summary>B-08: rootAVD LISTONLY dry-run preview.</summary>
    [RelayCommand]
    private async Task DryRunAsync()
    {
        IsBusy = true;
        Step = "Dry run…";
        try
        {
            await _root.EnsureRootAvdAsync(new Progress<string>(s => Step = s), default);
            await _root.DryRunAsync(new Progress<string>(s => Step = s), l => _log.Detail(l), default);
            _log.Info("Dry-run complete (no files were patched).");
        }
        catch (Exception ex) { _log.Error(ex.Message); }
        finally { IsBusy = false; Step = ""; }
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

    private Device? CurrentOnlineEmulator()
        => _monitor.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline);

    private async Task RefreshFridaStateAsync(string serial)
    {
        try
        {
            IsFridaRunning = await _frida.IsRunningAsync(serial);
            FridaStatus = IsFridaRunning ? "frida-server is running." : "frida-server is not running.";
        }
        catch (Exception ex)
        {
            IsFridaRunning = false;
            FridaStatus = "Frida status unavailable.";
            _log.Warning("Frida status check failed: " + ex.Message);
        }
    }

    private void ResetDownloadProgress()
    {
        HasDownloadProgress = false;
        IsDownloadProgressIndeterminate = true;
        DownloadProgress = 0;
        DownloadProgressText = "";
    }

    private void ReportDownloadProgress((long received, long? total) update)
    {
        HasDownloadProgress = true;
        if (update.total is > 0)
        {
            IsDownloadProgressIndeterminate = false;
            DownloadProgress = Math.Clamp(update.received * 100.0 / update.total.Value, 0, 100);
            DownloadProgressText = $"{FormatBytes(update.received)} / {FormatBytes(update.total.Value)} ({DownloadProgress:0}%)";
        }
        else
        {
            IsDownloadProgressIndeterminate = true;
            DownloadProgressText = $"{FormatBytes(update.received)} downloaded";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
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
