using CommunityToolkit.Mvvm.ComponentModel;

namespace AndroidEmulatorPlus.Models;

public sealed partial class AndroidApp : ObservableObject
{
    public required string Package { get; init; }
    public string? Label { get; init; }
    public string? VersionName { get; init; }
    public long VersionCode { get; init; }
    public bool IsSystem { get; init; }
    public bool IsDisabled { get; init; }

    [ObservableProperty] private long _dataSizeBytes;
    [ObservableProperty] private bool _isSelected;
    /// <summary>C-05: false = AndroidManifest declared allowBackup="false" on the source.</summary>
    [ObservableProperty] private bool _allowBackup = true;
    [ObservableProperty] private bool _migrateApk = true;
    [ObservableProperty] private bool _migrateInternal = true;
    [ObservableProperty] private bool _migrateExternal = true;
    [ObservableProperty] private bool _migrateObb;

    public string Display => string.IsNullOrEmpty(Label) ? Package : $"{Label} ({Package})";

    /// <summary>Tag rendered next to the package name (system / disabled / user).</summary>
    public string SourceTag => IsDisabled ? "disabled" : (IsSystem ? "system" : "user");

    public string SizeText => DataSizeBytes switch
    {
        <= 0 => "—",
        < 1024 => $"{DataSizeBytes} B",
        < 1024L * 1024 => $"{DataSizeBytes / 1024} KB",
        < 1024L * 1024 * 1024 => $"{DataSizeBytes / (1024 * 1024)} MB",
        _ => $"{DataSizeBytes / (1024L * 1024 * 1024)} GB",
    };

    partial void OnDataSizeBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));
}
