using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AndroidEmulatorPlus.Services;

public enum LogLevel { Info, Success, Warning, Error, Detail }

public sealed partial class LogEntry : ObservableObject
{
    public DateTime At { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public string Text { get; init; } = "";
    public string Display => $"[{At:HH:mm:ss}] {Text}";
}

public sealed class LogService
{
    public ObservableCollection<LogEntry> Entries { get; } = new();
    public event Action<LogEntry>? EntryAdded;

    // Rolling daily file at %LOCALAPPDATA%\AndroidEmulatorPlus\logs\app-YYYYMMDD.log.
    // The in-memory ring is capped at 2000 entries; the file keeps the full session
    // history so a post-mortem of a failed root/migrate is still possible.
    public string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "logs");

    public string CurrentLogFile => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    private readonly object _fileLock = new();
    private bool _firstWrite = true;

    private void Add(LogLevel level, string text)
    {
        var entry = new LogEntry { Level = level, Text = text };
        AppendToFile(entry);
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            d.BeginInvoke(() => { Entries.Add(entry); EntryAdded?.Invoke(entry); });
        else
        {
            Entries.Add(entry);
            EntryAdded?.Invoke(entry);
        }

        // Keep last 2000 in memory
        if (Entries.Count > 2000)
        {
            if (Application.Current?.Dispatcher is { } d2 && !d2.CheckAccess())
                d2.BeginInvoke(() => { while (Entries.Count > 2000) Entries.RemoveAt(0); });
            else
                while (Entries.Count > 2000) Entries.RemoveAt(0);
        }
    }

    private void AppendToFile(LogEntry entry)
    {
        try
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(LogDirectory);
                if (_firstWrite)
                {
                    File.AppendAllText(CurrentLogFile,
                        $"\n=== Session start {DateTime.Now:O} (pid {Environment.ProcessId}) " +
                        $"[rootAVD pin: {RootService.RootAvdPinnedRef}] ===\n");
                    _firstWrite = false;
                    PruneOldLogs();
                }
                File.AppendAllText(CurrentLogFile,
                    $"{entry.At:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Text}\n");
            }
        }
        catch { /* never let a log write crash the app */ }
    }

    private void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-14);
            foreach (var f in Directory.EnumerateFiles(LogDirectory, "app-*.log"))
            {
                try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    public void Info(string text) => Add(LogLevel.Info, text);
    public void Success(string text) => Add(LogLevel.Success, text);
    public void Warning(string text) => Add(LogLevel.Warning, text);
    public void Error(string text) => Add(LogLevel.Error, text);
    public void Detail(string text) => Add(LogLevel.Detail, text);

    public void Clear()
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            d.BeginInvoke(() => Entries.Clear());
        else
            Entries.Clear();
    }
}
