using System.Windows;
using System.Windows.Controls;
using AndroidEmulatorPlus.Services;
using AndroidEmulatorPlus.ViewModels;

namespace AndroidEmulatorPlus;

public partial class MainWindow : Window
{
    private readonly LogService _log;
    private readonly MainViewModel _vm;
    private readonly SettingsService _settings;
    private readonly SdkLocator _sdk;
    private readonly AvdService _avds;

    public MainWindow(LogService log, MainViewModel vm, SettingsService settings, SdkLocator sdk, AvdService avds)
    {
        _log = log;
        _vm = vm;
        _settings = settings;
        _sdk = sdk;
        _avds = avds;
        InitializeComponent();
        DataContext = vm;
        // Auto-scroll the log to the bottom as entries arrive.
        log.EntryAdded += _ =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (LogList.Template.FindName("LogScroll", LogList) is ScrollViewer sv)
                    sv.ScrollToEnd();
            });
        };
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // R-02 first-launch wizard: show once when there is no settings.json, no SDK,
        // or no AVDs yet. The user can dismiss with 'Don't show again' to persist
        // HasSeenWizard = true.
        if (_settings.Current.HasSeenWizard) return;
        if (_sdk.IsReady && _avds.List().Count > 0)
        {
            _settings.Current.HasSeenWizard = true;
            _settings.Save();
            return;
        }
        var dlg = new Views.WelcomeDialog(_vm, _settings, _sdk, _avds) { Owner = this };
        dlg.ShowDialog();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();
}
