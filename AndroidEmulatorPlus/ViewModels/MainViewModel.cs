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

    [ObservableProperty] private string _activeSection = "Avd";

    [ObservableProperty] private string _sdkStatusText = "";
    [ObservableProperty] private string _phoneStatusText = "no phone";
    [ObservableProperty] private string _emulatorStatusText = "no emulator";

    private readonly SdkLocator _sdk;
    private readonly DeviceMonitor _devices;

    public MainViewModel(LogService log,
        AvdViewModel avd, RootViewModel root, MigrateViewModel mig,
        AppsViewModel apps, ConfigViewModel cfg, InstallViewModel install,
        SdkLocator sdk, DeviceMonitor devices)
    {
        Log = log;
        AvdVm = avd;
        RootVm = root;
        MigrateVm = mig;
        AppsVm = apps;
        ConfigVm = cfg;
        InstallVm = install;
        _sdk = sdk;
        _devices = devices;
        _devices.Changed += OnDevicesChanged;
        RefreshSdk();
        Log.Info("AndroidEmulatorPlus v0.1.0 ready.");
    }

    private void OnDevicesChanged(IReadOnlyList<Device> devs)
    {
        var emu = devs.FirstOrDefault(d => d.IsEmulator);
        var phone = devs.FirstOrDefault(d => !d.IsEmulator);
        EmulatorStatusText = emu is null ? "no emulator" : $"{emu.Serial}";
        PhoneStatusText = phone is null ? "no phone" : phone.Display;
    }

    public void RefreshSdk()
    {
        _sdk.Refresh();
        SdkStatusText = _sdk.IsReady
            ? $"SDK ✓  {_sdk.SdkRoot}"
            : "SDK not found — open Install tab to set up.";
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
}
