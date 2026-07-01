// Auto-generated from Strings.resx — do not edit manually.
// Regenerate with: resgen or Visual Studio resx designer.

using System.Globalization;
using System.Resources;

namespace AndroidEmulatorPlus.Properties;

public static class Strings
{
    private static readonly ResourceManager _rm =
        new("AndroidEmulatorPlus.Properties.Strings",
            typeof(Strings).Assembly);

    public static string Get(string name) =>
        _rm.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    public static string AppTitle => Get(nameof(AppTitle));
    public static string SidebarWorkflow => Get(nameof(SidebarWorkflow));

    public static string TabInstall => Get(nameof(TabInstall));
    public static string TabAvds => Get(nameof(TabAvds));
    public static string TabRoot => Get(nameof(TabRoot));
    public static string TabMigrate => Get(nameof(TabMigrate));
    public static string TabApps => Get(nameof(TabApps));
    public static string TabConfigure => Get(nameof(TabConfigure));
    public static string TabLogcat => Get(nameof(TabLogcat));
    public static string TabConsole => Get(nameof(TabConsole));

    public static string TopBarPhone => Get(nameof(TopBarPhone));
    public static string TopBarEmulator => Get(nameof(TopBarEmulator));
    public static string BtnScreenshot => Get(nameof(BtnScreenshot));
    public static string BtnRecord => Get(nameof(BtnRecord));
    public static string BtnStopRecording => Get(nameof(BtnStopRecording));
    public static string BtnMirror => Get(nameof(BtnMirror));
    public static string BtnSettings => Get(nameof(BtnSettings));

    public static string InstallHeader => Get(nameof(InstallHeader));
    public static string InstallSubheader => Get(nameof(InstallSubheader));
    public static string InstallCurrentState => Get(nameof(InstallCurrentState));
    public static string InstallSdkRoot => Get(nameof(InstallSdkRoot));
    public static string InstallPlatformTools => Get(nameof(InstallPlatformTools));
    public static string InstallEmulator => Get(nameof(InstallEmulator));
    public static string InstallCmdlineTools => Get(nameof(InstallCmdlineTools));
    public static string InstallStatus => Get(nameof(InstallStatus));
    public static string BtnRefresh => Get(nameof(BtnRefresh));
    public static string BtnOpenSdkFolder => Get(nameof(BtnOpenSdkFolder));
    public static string BtnAcceptLicenses => Get(nameof(BtnAcceptLicenses));
    public static string BtnCheckUpdates => Get(nameof(BtnCheckUpdates));
    public static string BtnUpdatePackages => Get(nameof(BtnUpdatePackages));
    public static string BtnDownloadCmdlineTools => Get(nameof(BtnDownloadCmdlineTools));
    public static string BtnCancel => Get(nameof(BtnCancel));

    public static string AvdHeader => Get(nameof(AvdHeader));
    public static string AvdSubheader => Get(nameof(AvdSubheader));
    public static string BtnLaunch => Get(nameof(BtnLaunch));
    public static string BtnStop => Get(nameof(BtnStop));
    public static string BtnColdBoot => Get(nameof(BtnColdBoot));
    public static string BtnCreate => Get(nameof(BtnCreate));
    public static string BtnBrowseOnline => Get(nameof(BtnBrowseOnline));
    public static string AvdCreateHeader => Get(nameof(AvdCreateHeader));
    public static string AvdName => Get(nameof(AvdName));
    public static string AvdDeviceProfile => Get(nameof(AvdDeviceProfile));
    public static string AvdSystemImage => Get(nameof(AvdSystemImage));

    public static string RootHeader => Get(nameof(RootHeader));
    public static string BtnRootWithMagisk => Get(nameof(BtnRootWithMagisk));
    public static string BtnVerifyPolicy => Get(nameof(BtnVerifyPolicy));
    public static string BtnUnroot => Get(nameof(BtnUnroot));
    public static string BtnDryRun => Get(nameof(BtnDryRun));
    public static string BtnModules => Get(nameof(BtnModules));

    public static string MigrateHeader => Get(nameof(MigrateHeader));
    public static string MigrateSourcePhone => Get(nameof(MigrateSourcePhone));
    public static string MigrateTargetEmulator => Get(nameof(MigrateTargetEmulator));
    public static string BtnPair => Get(nameof(BtnPair));
    public static string BtnConnect => Get(nameof(BtnConnect));
    public static string BtnStartMigration => Get(nameof(BtnStartMigration));
    public static string MigrateScope => Get(nameof(MigrateScope));

    public static string AppsHeader => Get(nameof(AppsHeader));
    public static string BtnInstallApk => Get(nameof(BtnInstallApk));
    public static string BtnUninstall => Get(nameof(BtnUninstall));
    public static string BtnSelectAll => Get(nameof(BtnSelectAll));
    public static string BtnSelectNone => Get(nameof(BtnSelectNone));

    public static string ConfigHeader => Get(nameof(ConfigHeader));
    public static string ConfigHardware => Get(nameof(ConfigHardware));
    public static string ConfigRam => Get(nameof(ConfigRam));
    public static string ConfigCpuCores => Get(nameof(ConfigCpuCores));
    public static string ConfigDisk => Get(nameof(ConfigDisk));
    public static string BtnSaveConfig => Get(nameof(BtnSaveConfig));
    public static string BtnResizeDisk => Get(nameof(BtnResizeDisk));
    public static string BtnResizeAndWipe => Get(nameof(BtnResizeAndWipe));

    public static string LogcatHeader => Get(nameof(LogcatHeader));
    public static string BtnStartStream => Get(nameof(BtnStartStream));
    public static string BtnStopStream => Get(nameof(BtnStopStream));
    public static string BtnClearBuffer => Get(nameof(BtnClearBuffer));
    public static string BtnClearView => Get(nameof(BtnClearView));
    public static string BtnSaveLog => Get(nameof(BtnSaveLog));

    public static string ConsoleHeader => Get(nameof(ConsoleHeader));
    public static string ConsoleSectionGps => Get(nameof(ConsoleSectionGps));
    public static string ConsoleSectionBattery => Get(nameof(ConsoleSectionBattery));
    public static string ConsoleSectionTelephony => Get(nameof(ConsoleSectionTelephony));
    public static string ConsoleSectionNetwork => Get(nameof(ConsoleSectionNetwork));
    public static string ConsoleSectionClipboard => Get(nameof(ConsoleSectionClipboard));
    public static string ConsoleSectionSensors => Get(nameof(ConsoleSectionSensors));
    public static string ConsoleSectionRawCommand => Get(nameof(ConsoleSectionRawCommand));
    public static string BtnSetLocation => Get(nameof(BtnSetLocation));
    public static string BtnApplyBattery => Get(nameof(BtnApplyBattery));
    public static string BtnSend => Get(nameof(BtnSend));

    public static string SettingsTitle => Get(nameof(SettingsTitle));
    public static string SettingsAppearance => Get(nameof(SettingsAppearance));
    public static string SettingsTheme => Get(nameof(SettingsTheme));
    public static string SettingsPaths => Get(nameof(SettingsPaths));
    public static string SettingsNetwork => Get(nameof(SettingsNetwork));
    public static string SettingsBehavior => Get(nameof(SettingsBehavior));
    public static string SettingsUpdates => Get(nameof(SettingsUpdates));
    public static string SettingsTelemetry => Get(nameof(SettingsTelemetry));
    public static string BtnSave => Get(nameof(BtnSave));

    public static string NoPhoneConnected => Get(nameof(NoPhoneConnected));
    public static string NoEmulatorConnected => Get(nameof(NoEmulatorConnected));
    public static string BtnClose => Get(nameof(BtnClose));
    public static string BtnOk => Get(nameof(BtnOk));
    public static string Running => Get(nameof(Running));
}
