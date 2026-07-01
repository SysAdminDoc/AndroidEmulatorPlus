using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidEmulatorPlus.ViewModels;

/// <summary>
/// Drives the Console tab: sensor / GPS / battery / telephony / network simulation
/// against a running emulator. All commands go through ConsoleService.SendAsync.
/// </summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    private readonly ConsoleService _console;
    private readonly AdbService _adb;
    private readonly DeviceMonitor _monitor;
    private readonly NetworkProfileService _networkProfiles;
    private readonly LogService _log;

    [ObservableProperty] private double _gpsLatitude = 37.422;
    [ObservableProperty] private double _gpsLongitude = -122.084;
    [ObservableProperty] private int _batteryPercent = 100;
    [ObservableProperty] private string _powerStatus = "full";
    [ObservableProperty] private string _gsmNumber = "5551234";
    [ObservableProperty] private string _smsNumber = "5551234";
    [ObservableProperty] private string _smsBody = "Test from AEP";
    [ObservableProperty] private string _networkSpeed = "full";
    [ObservableProperty] private string _networkDelay = "none";
    [ObservableProperty] private NetworkProfile? _selectedNetworkProfile;
    [ObservableProperty] private string _freeFormArgs = "";
    [ObservableProperty] private string _lastResult = "";
    [ObservableProperty] private string _networkInfoText = "";
    [ObservableProperty] private bool _hasNetworkInfo;
    [ObservableProperty] private double _accelX;
    [ObservableProperty] private double _accelY = 9.8;
    [ObservableProperty] private double _accelZ;
    [ObservableProperty] private double _gyroX;
    [ObservableProperty] private double _gyroY;
    [ObservableProperty] private double _gyroZ;
    [ObservableProperty] private bool _clipboardSyncEnabled;
    [ObservableProperty] private string _clipboardSyncStatus = "Off";

    private CancellationTokenSource? _clipSyncCts;
    private string _lastEmuClip = "";
    private string _lastHostClip = "";

    public IReadOnlyList<string> PowerStates { get; } = new[] { "full", "charging", "discharging", "not-charging", "unknown" };
    public IReadOnlyList<string> Speeds { get; } = new[] { "full", "gsm", "edge", "umts", "hsdpa", "lte" };
    public IReadOnlyList<string> Delays { get; } = new[] { "none", "gprs", "edge", "umts" };
    public IReadOnlyList<NetworkProfile> NetworkProfiles => _networkProfiles.Profiles;

    public ConsoleViewModel(ConsoleService console, AdbService adb, DeviceMonitor monitor,
        NetworkProfileService networkProfiles, LogService log)
    {
        _console = console;
        _adb = adb;
        _monitor = monitor;
        _networkProfiles = networkProfiles;
        _log = log;
    }

    partial void OnSelectedNetworkProfileChanged(NetworkProfile? value)
    {
        if (value is null) return;
        NetworkSpeed = value.Speed;
        NetworkDelay = value.Delay;
    }

    private string? ActiveSerial() => _monitor.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline)?.Serial;

    [RelayCommand]
    private async Task SetGeoAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        var r = await _console.GeoFixAsync(s, GpsLongitude, GpsLatitude);
        LastResult = r.Combined.Trim();
        if (r.Success) _log.Info($"geo fix {GpsLongitude},{GpsLatitude}");
    }

    [RelayCommand]
    private async Task SetBatteryAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        var r1 = await _console.PowerCapacityAsync(s, BatteryPercent);
        var r2 = await _console.PowerStatusAsync(s, PowerStatus);
        LastResult = (r1.Combined + "\n" + r2.Combined).Trim();
        if (r1.Success && r2.Success) _log.Info($"power capacity {BatteryPercent}, status {PowerStatus}");
    }

    [RelayCommand]
    private async Task RingAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        var r = await _console.GsmCallAsync(s, GsmNumber);
        LastResult = r.Combined.Trim();
        if (r.Success) _log.Info($"gsm call {GsmNumber}");
    }

    [RelayCommand]
    private async Task SendSmsAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        var r = await _console.SmsSendAsync(s, SmsNumber, SmsBody);
        LastResult = r.Combined.Trim();
        if (r.Success) _log.Info($"sms send {SmsNumber}");
    }

    [RelayCommand]
    private async Task SetNetworkAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        var r1 = await _console.NetworkSpeedAsync(s, NetworkSpeed);
        var r2 = await _console.NetworkDelayAsync(s, NetworkDelay);
        LastResult = (r1.Combined + "\n" + r2.Combined).Trim();
        if (r1.Success && r2.Success) _log.Info($"network speed {NetworkSpeed}, delay {NetworkDelay}");
    }

    [RelayCommand]
    private async Task SetAccelerometerAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        var r = await _console.SendAsync(s, new[] { "sensor", "set", "acceleration",
            $"{AccelX:F2}:{AccelY:F2}:{AccelZ:F2}" });
        LastResult = r.Combined.Trim();
        if (r.Success) _log.Info($"acceleration {AccelX:F2}:{AccelY:F2}:{AccelZ:F2}");
    }

    [RelayCommand]
    private async Task SetGyroscopeAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        var r = await _console.SendAsync(s, new[] { "sensor", "set", "gyroscope",
            $"{GyroX:F2}:{GyroY:F2}:{GyroZ:F2}" });
        LastResult = r.Combined.Trim();
        if (r.Success) _log.Info($"gyroscope {GyroX:F2}:{GyroY:F2}:{GyroZ:F2}");
    }

    [RelayCommand]
    private async Task ShowNetworkInfoAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        var r = await _adb.ShellAsync(s, "ip -4 addr show | grep inet");
        var lines = new List<string>();
        lines.Add($"Serial: {s}");
        foreach (var line in r.StdOut.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed)) lines.Add(trimmed);
        }

        var devices = _monitor.Current.Where(d => d.IsEmulator && d.IsOnline).ToList();
        if (devices.Count > 1)
        {
            lines.Add("");
            lines.Add($"Peer AVDs: {devices.Count} emulators running");
            foreach (var d in devices)
                lines.Add($"  {d.Serial} ({d.Model})");
            lines.Add("");
            lines.Add("Peer connectivity test:");
            lines.Add($"  adb -s {s} shell ping -c 3 10.0.2.15");
        }
        NetworkInfoText = string.Join("\n", lines);
        HasNetworkInfo = true;
    }

    [RelayCommand]
    private async Task SendFreeFormAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        if (string.IsNullOrWhiteSpace(FreeFormArgs)) return;
        IReadOnlyList<string> args;
        try { args = ConsoleService.ParseEmuArgs(FreeFormArgs); }
        catch (FormatException ex) { _log.Warning(ex.Message); return; }
        var r = await _console.SendAsync(s, args);
        LastResult = r.Combined.Trim();
    }

    [RelayCommand]
    private void ToggleClipboardSync()
    {
        if (ClipboardSyncEnabled)
        {
            _clipSyncCts?.Cancel();
            _clipSyncCts?.Dispose();
            _clipSyncCts = null;
            ClipboardSyncEnabled = false;
            ClipboardSyncStatus = "Off";
            _log.Info("Clipboard sync stopped.");
            return;
        }
        ClipboardSyncEnabled = true;
        ClipboardSyncStatus = "Syncing...";
        _clipSyncCts = new CancellationTokenSource();
        _ = ClipboardSyncLoopAsync(_clipSyncCts.Token);
        _log.Info("Clipboard sync started.");
    }

    private async Task ClipboardSyncLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
                if (ActiveSerial() is not { } s) continue;

                try
                {
                    var hostText = "";
                    if (System.Windows.Application.Current?.Dispatcher is { } d)
                        hostText = await d.InvokeAsync(() =>
                        {
                            try { return System.Windows.Clipboard.GetText(); } catch { return ""; }
                        });

                    if (!string.IsNullOrEmpty(hostText) && hostText != _lastHostClip)
                    {
                        _lastHostClip = hostText;
                        await _console.ClipboardSetAsync(_adb, s, hostText, ct);
                        ClipboardSyncStatus = "Host → Emu";
                    }

                    var emuR = await _console.ClipboardGetAsync(_adb, s, ct);
                    var emuText = emuR.Success ? emuR.StdOut.Trim() : "";
                    if (!string.IsNullOrEmpty(emuText) && emuText != _lastEmuClip && emuText != _lastHostClip)
                    {
                        _lastEmuClip = emuText;
                        if (System.Windows.Application.Current?.Dispatcher is { } d2)
                            await d2.InvokeAsync(() =>
                            {
                                try { System.Windows.Clipboard.SetText(emuText); } catch { }
                            });
                        ClipboardSyncStatus = "Emu → Host";
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            ClipboardSyncEnabled = false;
            ClipboardSyncStatus = "Off";
        }
    }

    /// <summary>B-07 clipboard helpers — manual paste/pull, not background sync.</summary>
    [RelayCommand]
    private async Task PullClipboardAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        var r = await _console.ClipboardGetAsync(_adb, s);
        if (r.Success)
        {
            try { System.Windows.Clipboard.SetText(r.StdOut.Trim()); }
            catch { /* ignored */ }
            LastResult = "Pulled from emulator → host clipboard.";
            _log.Info("clipboard pulled");
        }
        else LastResult = "Pull failed: " + r.Combined.Trim();
    }

    [RelayCommand]
    private async Task PushClipboardAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        string text = "";
        try { text = System.Windows.Clipboard.GetText(); } catch { }
        if (string.IsNullOrEmpty(text)) { LastResult = "Host clipboard is empty."; return; }
        var r = await _console.ClipboardSetAsync(_adb, s, text);
        LastResult = r.Success ? "Pushed host clipboard → emulator." : ("Push failed: " + r.Combined.Trim());
        if (r.Success) _log.Info("clipboard pushed");
    }
}
