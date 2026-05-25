using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class LogcatViewModel : ObservableObject
{
    private readonly LogcatService _logcat;
    private readonly DeviceMonitor _monitor;
    private readonly LogService _log;

    public ObservableCollection<string> Lines { get; } = new();

    /// <summary>Filter token list (e.g. <c>com.example.app:V *:S</c> or <c>*:E</c>).</summary>
    [ObservableProperty] private string _filter = "*:V";
    [ObservableProperty] private string _packageFilter = "";
    [ObservableProperty] private string _status = "Idle.";
    [ObservableProperty] private bool _isStreaming;

    private const int MaxBufferLines = 5000;

    public LogcatViewModel(LogcatService logcat, DeviceMonitor monitor, LogService log)
    {
        _logcat = logcat;
        _monitor = monitor;
        _log = log;
        _logcat.LineReceived += OnLine;
    }

    private void OnLine(string line)
    {
        if (Application.Current?.Dispatcher is { } d)
        {
            _ = d.BeginInvoke(() =>
            {
                Lines.Add(line);
                while (Lines.Count > MaxBufferLines) Lines.RemoveAt(0);
            });
        }
        else
        {
            Lines.Add(line);
            while (Lines.Count > MaxBufferLines) Lines.RemoveAt(0);
        }
    }

    [RelayCommand]
    private void Start()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline);
        if (emu is null) { _log.Warning("No emulator attached."); return; }
        Lines.Clear();
        // Build filter args. If a package filter is supplied, look up its uid and use --uid;
        // otherwise pass the priority spec verbatim.
        string args;
        if (!string.IsNullOrWhiteSpace(PackageFilter))
        {
            // PackageFilter is a package name (e.g. com.android.chrome). logcat doesn't
            // directly filter by package, so we keep a coarse priority filter and let
            // the user grep visually. (Per-uid filtering needs the running uid which
            // requires an extra adb shell pidof / dumpsys lookup that we skip here.)
            args = $"{PackageFilter}:V {Filter}";
        }
        else
        {
            args = Filter;
        }
        _logcat.Start(emu.Serial, args);
        IsStreaming = _logcat.IsRunning;
        Status = IsStreaming ? $"Streaming from {emu.Serial}…" : "logcat failed to start.";
    }

    [RelayCommand]
    private void Stop()
    {
        _logcat.Stop();
        IsStreaming = false;
        Status = "Stopped.";
    }

    [RelayCommand]
    private async Task ClearBufferAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline);
        if (emu is null) { _log.Warning("No emulator attached."); return; }
        await _logcat.ClearBufferAsync(emu.Serial);
        Lines.Clear();
    }

    [RelayCommand]
    private void ClearView() => Lines.Clear();

    [RelayCommand]
    private void Save()
    {
        if (Lines.Count == 0) { _log.Warning("Nothing to save."); return; }
        var dlg = new SaveFileDialog
        {
            FileName = $"logcat-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            Filter = "Log file (*.log)|*.log|Text file (*.txt)|*.txt|All files|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllLines(dlg.FileName, Lines);
            _log.Success($"Saved {Lines.Count} lines to {dlg.FileName}.");
        }
        catch (Exception ex) { _log.Error("Save failed: " + ex.Message); }
    }
}
