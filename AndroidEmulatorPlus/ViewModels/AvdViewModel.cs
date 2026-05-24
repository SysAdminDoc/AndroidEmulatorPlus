using System.Collections.ObjectModel;
using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class AvdViewModel : ObservableObject
{
    private readonly AvdService _avds;
    private readonly EmulatorService _emu;
    private readonly LogService _log;
    private readonly SdkLocator _sdk;

    public ObservableCollection<Avd> Avds { get; } = new();
    [ObservableProperty] private Avd? _selected;
    [ObservableProperty] private bool _isBusy;

    // Create form
    [ObservableProperty] private string _newName = "MyEmulator";
    [ObservableProperty] private string _newDevice = "pixel_7";
    [ObservableProperty] private string? _newImage;
    public ObservableCollection<string> AvailableImages { get; } = new();
    public ObservableCollection<string> AvailableDevices { get; } = new()
    {
        "pixel_7", "pixel_7_pro", "pixel_8", "pixel_8_pro", "pixel_fold", "pixel_tablet",
        "Nexus 6P", "Nexus 5X", "small_phone",
    };

    public AvdViewModel(AvdService avds, EmulatorService emu, LogService log, SdkLocator sdk)
    {
        _avds = avds;
        _emu = emu;
        _log = log;
        _sdk = sdk;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Avds.Clear();
            foreach (var a in _avds.List()) Avds.Add(a);
            AvailableImages.Clear();
            foreach (var img in await _avds.ListSystemImagesAsync())
                AvailableImages.Add(img);
            NewImage ??= AvailableImages.LastOrDefault();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Launch(Avd? avd)
    {
        if (avd is null) return;
        _emu.Launch(avd.Name);
    }

    [RelayCommand]
    private void LaunchCold(Avd? avd)
    {
        if (avd is null) return;
        _emu.Launch(avd.Name, coldBoot: true);
    }

    [RelayCommand]
    private async Task DeleteAsync(Avd? avd)
    {
        if (avd is null) return;
        IsBusy = true;
        try
        {
            var r = await _avds.DeleteAsync(avd.Name);
            if (r.Success) _log.Success($"Deleted AVD '{avd.Name}'.");
            else _log.Error("Delete failed: " + r.Combined.Trim());
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewImage))
        {
            _log.Warning("Pick a name and a system image first.");
            return;
        }
        IsBusy = true;
        try
        {
            var r = await _avds.CreateAsync(NewName.Trim(), NewImage!, NewDevice);
            if (r.Success) _log.Success($"Created AVD '{NewName}'.");
            else _log.Error("avdmanager: " + r.Combined.Trim());
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }
}
