using System.Diagnostics;
using System.IO;
using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public LogService Log { get; }
    public AvdViewModel AvdVm { get; }
    public RootViewModel RootVm { get; }
    public MigrateViewModel MigrateVm { get; }
    public AppsViewModel AppsVm { get; }
    public ConfigViewModel ConfigVm { get; }
    public InstallViewModel InstallVm { get; }
    public LogcatViewModel LogcatVm { get; }
    public ConsoleViewModel ConsoleVm { get; }

    [ObservableProperty] private string _activeSection = "Avd";

    [ObservableProperty] private string _sdkStatusText = "";
    [ObservableProperty] private string _sdkDetailText = "";
    [ObservableProperty] private string _phoneStatusText = "no phone";
    [ObservableProperty] private string _emulatorStatusText = "no emulator";
    [ObservableProperty] private bool _hasEmulatorAttached;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _recordButtonText = "Record";

    private readonly SdkLocator _sdk;
    private readonly DeviceMonitor _devices;
    private readonly AdbService _adb;
    private readonly ScreenRecordService _record;
    private readonly SettingsService _settings;
    private readonly ScrcpyService _scrcpy;

    public MainViewModel(LogService log,
        AvdViewModel avd, RootViewModel root, MigrateViewModel mig,
        AppsViewModel apps, ConfigViewModel cfg, InstallViewModel install, LogcatViewModel logcat, ConsoleViewModel console,
        SdkLocator sdk, DeviceMonitor devices, AdbService adb, ScreenRecordService record, SettingsService settings, ScrcpyService scrcpy)
    {
        Log = log;
        AvdVm = avd;
        RootVm = root;
        MigrateVm = mig;
        AppsVm = apps;
        ConfigVm = cfg;
        InstallVm = install;
        LogcatVm = logcat;
        ConsoleVm = console;
        _sdk = sdk;
        _devices = devices;
        _adb = adb;
        _record = record;
        _settings = settings;
        _scrcpy = scrcpy;
        _devices.Changed += OnDevicesChanged;
        RefreshSdk();
        // If the SDK isn't there yet, land on Install rather than the empty AVDs list.
        if (!_sdk.IsReady) _activeSection = "Install";
        Log.Info("AndroidEmulatorPlus v0.2.0 ready.");
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var dlg = new Views.SettingsDialog(_settings)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        dlg.ShowDialog();

        // C-10: if Settings → "Show welcome wizard…" reset HasSeenWizard, reopen it.
        if (!_settings.Current.HasSeenWizard)
        {
            var sdk = App.Services.GetService(typeof(SdkLocator)) as SdkLocator;
            var avds = App.Services.GetService(typeof(AvdService)) as AvdService;
            if (sdk is not null && avds is not null)
            {
                var w = new Views.WelcomeDialog(this, _settings, sdk, avds)
                {
                    Owner = System.Windows.Application.Current?.MainWindow,
                };
                w.ShowDialog();
            }
        }
    }

    private string? _lastSeenEmuSerial;
    private void OnDevicesChanged(IReadOnlyList<Device> devs)
    {
        var emu = devs.FirstOrDefault(d => d.IsEmulator);
        var phone = devs.FirstOrDefault(d => !d.IsEmulator);
        EmulatorStatusText = emu is null ? "no emulator" : $"{emu.Serial}";
        PhoneStatusText = phone is null ? "no phone" : phone.Display;
        HasEmulatorAttached = emu is not null && emu.IsOnline;

        // C-16: auto-launch scrcpy when a new emulator comes online.
        var currentSerial = emu?.IsOnline == true ? emu.Serial : null;
        if (_settings.Current.AutoScrcpy
            && currentSerial is not null
            && currentSerial != _lastSeenEmuSerial)
        {
            try { _scrcpy.Launch(currentSerial); } catch { }
        }
        _lastSeenEmuSerial = currentSerial;
    }

    private string MediaDir => !string.IsNullOrWhiteSpace(_settings.Current.MediaDir)
        ? _settings.Current.MediaDir!
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AndroidEmulatorPlus");

    [RelayCommand]
    private async Task ScreenshotAsync()
    {
        var emu = _devices.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline);
        if (emu is null) { Log.Warning("No emulator attached."); return; }
        var dir = MediaDir;
        var dest = Path.Combine(dir, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        Log.Info($"Taking screenshot from {emu.Serial}…");
        bool ok;
        try { ok = await _adb.ScreenshotAsync(emu.Serial, dest); }
        catch (Exception ex)
        {
            Log.Error("Screenshot failed: " + ex.Message);
            return;
        }
        if (ok)
        {
            Log.Success($"Saved: {dest}");
            try { Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warning("Couldn't open viewer: " + ex.Message); }
        }
        else Log.Error("Screenshot failed.");
    }

    public void RefreshSdk()
    {
        _sdk.Refresh();
        SdkStatusText = _sdk.IsReady ? "Ready" : "Not found";
        SdkDetailText = _sdk.IsReady
            ? _sdk.SdkRoot ?? "SDK detected."
            : "Open the Install / SDK tab to set up Android tooling.";
    }

    [RelayCommand]
    private void Navigate(string section)
    {
        ActiveSection = section;
        if (section == "Avd")    AvdVm.RefreshCommand.Execute(null);
        if (section == "Apps")   AppsVm.RefreshCommand.Execute(null);
        if (section == "Config") ConfigVm.RefreshCommand.Execute(null);
        if (section == "Root")   RootVm.RefreshCommand.Execute(null);
        if (section == "Migrate")MigrateVm.RefreshCommand.Execute(null);
        if (section == "Install")InstallVm.RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private void LaunchScrcpy()
    {
        var emu = _devices.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline);
        if (emu is null) { Log.Warning("No emulator attached."); return; }
        _scrcpy.Launch(emu.Serial);
    }

    /// <summary>F5 — refresh whatever tab is in focus.</summary>
    [RelayCommand]
    private void RefreshActive()
    {
        switch (ActiveSection)
        {
            case "Avd":     AvdVm.RefreshCommand.Execute(null); break;
            case "Apps":    AppsVm.RefreshCommand.Execute(null); break;
            case "Config":  ConfigVm.RefreshCommand.Execute(null); break;
            case "Root":    RootVm.RefreshCommand.Execute(null); break;
            case "Migrate": MigrateVm.RefreshCommand.Execute(null); break;
            case "Install": InstallVm.RefreshCommand.Execute(null); break;
            case "Logcat":  /* logcat has start/stop, no refresh */ break;
            case "Console": /* console is request/response, no refresh */ break;
        }
    }

    /// <summary>Ctrl+L — clear the in-app log panel.</summary>
    [RelayCommand]
    private void ClearLog() => Log.Clear();

    /// <summary>
    /// A-14: Start/stop screen recording from the top-bar toggle. Saves to
    /// %USERPROFILE%/Pictures/AndroidEmulatorPlus/ once stopped.
    /// </summary>
    [RelayCommand]
    private async Task RecordAsync()
    {
        if (_record.IsRecording)
        {
            var dir = MediaDir;
            string? local = null;
            try { local = await _record.StopAsync(dir); }
            catch (Exception ex) { Log.Error("Recording stop failed: " + ex.Message); }
            finally
            {
                IsRecording = false;
                RecordButtonText = "Record";
            }
            if (local is not null)
            {
                try { Process.Start(new ProcessStartInfo(System.IO.Path.GetDirectoryName(local)!) { UseShellExecute = true }); }
                catch { }
            }
            return;
        }
        var emu = _devices.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline);
        if (emu is null) { Log.Warning("No emulator attached."); return; }
        var remote = _record.Start(emu.Serial);
        if (remote is null) return;
        IsRecording = true;
        RecordButtonText = "Stop recording";
    }
}
