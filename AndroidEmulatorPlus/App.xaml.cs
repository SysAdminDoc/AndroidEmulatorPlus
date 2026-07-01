using System.Windows;
using System.Windows.Threading;
using AndroidEmulatorPlus.Services;
using AndroidEmulatorPlus.ViewModels;
using AndroidEmulatorPlus.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidEmulatorPlus;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Load the chosen palette + styles BEFORE any view binds. Theme choice
        // persists to settings.json; ThemeService can swap the palette live later.
        var theme = SettingsService.ReadThemeFromDisk();
        var paletteUri = theme switch
        {
            "latte" => "Themes/Latte.xaml",
            "frappe" => "Themes/Frappe.xaml",
            "macchiato" => "Themes/Macchiato.xaml",
            _ => "Themes/Mocha.xaml",
        };
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(paletteUri, UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Styles.xaml", UriKind.Relative) });

        var sc = new ServiceCollection();

        sc.AddSingleton<LogService>();
        sc.AddSingleton<SettingsService>();
        sc.AddSingleton<ThemeService>();
        sc.AddSingleton<UpdateService>();
        sc.AddSingleton<SdkLocator>();
        sc.AddSingleton<AdbService>();
        sc.AddSingleton<EmulatorService>();
        sc.AddSingleton<AvdService>();
        sc.AddSingleton<QemuImgService>();
        sc.AddSingleton<RootService>();
        sc.AddSingleton<MigrationService>();
        sc.AddSingleton<AppService>();
        sc.AddSingleton<ConfigService>();
        sc.AddSingleton<DownloadService>();
        sc.AddSingleton<HashVerificationService>();
        sc.AddSingleton<CacheDiagnosticsService>();
        sc.AddSingleton<LogcatService>();
        sc.AddSingleton<SdkmanagerService>();
        sc.AddSingleton<PresetService>();
        sc.AddSingleton<SnapshotService>();
        sc.AddSingleton<ApkSignerService>();
        sc.AddSingleton<ScrcpyService>();
        sc.AddSingleton<ConsoleService>();
        sc.AddSingleton<NetworkProfileService>();
        sc.AddSingleton<MagiskService>();
        sc.AddSingleton<BundleInstallerService>();
        sc.AddSingleton<ScreenRecordService>();
        sc.AddSingleton<CaCertService>();
        sc.AddSingleton<FridaService>();
        sc.AddSingleton<ReleasePreflightService>();
        sc.AddSingleton<AvdTemplateService>();
        sc.AddSingleton<HandoffExportService>();
        sc.AddSingleton<SupportBundleService>();
        sc.AddSingleton<ToastService>();
        sc.AddSingleton<DeviceMonitor>();

        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<AvdViewModel>();
        sc.AddSingleton<RootViewModel>();
        sc.AddSingleton<MigrateViewModel>();
        sc.AddSingleton<AppsViewModel>();
        sc.AddSingleton<ConfigViewModel>();
        sc.AddSingleton<InstallViewModel>();
        sc.AddSingleton<LogcatViewModel>();
        sc.AddSingleton<ConsoleViewModel>();

        sc.AddTransient<MainWindow>();

        Services = sc.BuildServiceProvider();

        DispatcherUnhandledException += OnUnhandledException;

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();

        Services.GetRequiredService<DeviceMonitor>().Start();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Services.GetService<LogcatService>()?.Dispose(); } catch { }
        try { Services.GetService<ScreenRecordService>()?.Dispose(); } catch { }
        try { Services.GetService<DownloadService>()?.Dispose(); } catch { }
        try { Services.GetService<EmulatorService>()?.KillAll(); } catch { }
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var log = Services.GetService<LogService>();
        log?.Error($"Unhandled: {e.Exception.GetType().Name}: {e.Exception.Message}");
        try
        {
            var crashPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AndroidEmulatorPlus", "crash.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(crashPath)!);
            System.IO.File.AppendAllText(crashPath, $"\n[{DateTime.Now:O}] {e.Exception}\n");
        }
        catch { }
        MessageBox.Show($"Unexpected error: {e.Exception.Message}\n\nDetails written to crash.log.",
            "AndroidEmulatorPlus", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
