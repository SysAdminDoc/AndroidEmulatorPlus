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
    [ObservableProperty] private string _freeFormArgs = "";
    [ObservableProperty] private string _lastResult = "";

    public IReadOnlyList<string> PowerStates { get; } = new[] { "full", "charging", "discharging", "not-charging", "unknown" };
    public IReadOnlyList<string> Speeds { get; } = new[] { "full", "gsm", "edge", "umts", "hsdpa", "lte" };
    public IReadOnlyList<string> Delays { get; } = new[] { "none", "gprs", "edge", "umts" };

    public ConsoleViewModel(ConsoleService console, AdbService adb, DeviceMonitor monitor, LogService log)
    {
        _console = console;
        _adb = adb;
        _monitor = monitor;
        _log = log;
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
    private async Task SendFreeFormAsync()
    {
        if (ActiveSerial() is not { } s) { _log.Warning("No emulator attached."); return; }
        if (string.IsNullOrWhiteSpace(FreeFormArgs)) return;
        var args = FreeFormArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var r = await _console.SendAsync(s, args);
        LastResult = r.Combined.Trim();
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
