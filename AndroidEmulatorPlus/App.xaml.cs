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
        var sc = new ServiceCollection();

        sc.AddSingleton<LogService>();
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
        sc.AddSingleton<DeviceMonitor>();

        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<AvdViewModel>();
        sc.AddSingleton<RootViewModel>();
        sc.AddSingleton<MigrateViewModel>();
        sc.AddSingleton<AppsViewModel>();
        sc.AddSingleton<ConfigViewModel>();
        sc.AddSingleton<InstallViewModel>();

        sc.AddTransient<MainWindow>();

        Services = sc.BuildServiceProvider();

        DispatcherUnhandledException += OnUnhandledException;

        var window = Services.GetRequiredService<MainWindow>();
        window.DataContext = Services.GetRequiredService<MainViewModel>();
        window.Show();

        Services.GetRequiredService<DeviceMonitor>().Start();

        base.OnStartup(e);
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
