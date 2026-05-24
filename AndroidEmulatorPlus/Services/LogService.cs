using System.Collections.ObjectModel;
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

    private void Add(LogLevel level, string text)
    {
        var entry = new LogEntry { Level = level, Text = text };
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
