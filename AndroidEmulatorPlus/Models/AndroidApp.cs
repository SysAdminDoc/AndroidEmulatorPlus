using CommunityToolkit.Mvvm.ComponentModel;

namespace AndroidEmulatorPlus.Models;

public sealed partial class AndroidApp : ObservableObject
{
    public required string Package { get; init; }
    public string? Label { get; init; }
    public string? VersionName { get; init; }
    public long VersionCode { get; init; }
    public bool IsSystem { get; init; }
    public long DataSizeBytes { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    public string Display => string.IsNullOrEmpty(Label) ? Package : $"{Label} ({Package})";

    public string SizeText => DataSizeBytes switch
    {
        < 1024 => "—",
        < 1024L * 1024 => $"{DataSizeBytes / 1024} KB",
        < 1024L * 1024 * 1024 => $"{DataSizeBytes / (1024 * 1024)} MB",
        _ => $"{DataSizeBytes / (1024L * 1024 * 1024)} GB",
    };
}
