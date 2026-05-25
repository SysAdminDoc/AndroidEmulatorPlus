using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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
    private readonly object _pendingLock = new();
    private readonly List<string> _pendingLines = new();
    private readonly DispatcherTimer? _flushTimer;

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
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            _flushTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(100),
            };
            _flushTimer.Tick += (_, _) => FlushPendingLines();
        }
    }

    private void OnLine(string line)
    {
        lock (_pendingLock) _pendingLines.Add(line);
        if (_flushTimer is null) FlushPendingLines();
    }

    private void FlushPendingLines()
    {
        if (Application.Current?.Dispatcher is { } d)
        {
            if (d.CheckAccess())
            {
                foreach (var pendingLine in DrainPendingLines()) Lines.Add(pendingLine);
                TrimLines();
                return;
            }
            _ = d.BeginInvoke(() =>
            {
                foreach (var pendingLine in DrainPendingLines()) Lines.Add(pendingLine);
                TrimLines();
            });
        }
        else
        {
            foreach (var pendingLine in DrainPendingLines()) Lines.Add(pendingLine);
            TrimLines();
        }
    }

    private List<string> DrainPendingLines()
    {
        lock (_pendingLock)
        {
            if (_pendingLines.Count == 0) return new();
            var batch = new List<string>(_pendingLines);
            _pendingLines.Clear();
            return batch;
        }
    }

    private void TrimLines()
    {
        while (Lines.Count > MaxBufferLines) Lines.RemoveAt(0);
    }

    private void ClearPendingLines()
    {
        lock (_pendingLock) _pendingLines.Clear();
    }

    [RelayCommand]
    private void Start()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline);
        if (emu is null) { _log.Warning("No emulator attached."); return; }
        Lines.Clear();
        ClearPendingLines();
        _flushTimer?.Start();
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
        if (!IsStreaming) _flushTimer?.Stop();
        Status = IsStreaming ? $"Streaming from {emu.Serial}…" : "logcat failed to start.";
    }

    [RelayCommand]
    private void Stop()
    {
        _logcat.Stop();
        _flushTimer?.Stop();
        FlushPendingLines();
        IsStreaming = false;
        Status = "Stopped.";
    }

    [RelayCommand]
    private async Task ClearBufferAsync()
    {
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator && d.IsOnline);
        if (emu is null) { _log.Warning("No emulator attached."); return; }
        await _logcat.ClearBufferAsync(emu.Serial);
        ClearPendingLines();
        Lines.Clear();
    }

    [RelayCommand]
    private void ClearView()
    {
        ClearPendingLines();
        Lines.Clear();
    }

    [RelayCommand]
    private void Save()
    {
        FlushPendingLines();
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
