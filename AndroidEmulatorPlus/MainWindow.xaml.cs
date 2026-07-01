using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    private readonly UpdateService _updates;

    public MainWindow(LogService log, MainViewModel vm, SettingsService settings, SdkLocator sdk, AvdService avds, UpdateService updates)
    {
        _log = log;
        _vm = vm;
        _settings = settings;
        _sdk = sdk;
        _avds = avds;
        _updates = updates;
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
        if (_settings.Current.HasSeenWizard)
        {
            StartBackgroundUpdateCheck();
            return;
        }
        if (_sdk.IsReady && _avds.List().Count > 0)
        {
            _settings.Current.HasSeenWizard = true;
            _settings.Save();
            StartBackgroundUpdateCheck();
            return;
        }
        var dlg = new Views.WelcomeDialog(_vm, _settings, _sdk, _avds) { Owner = this };
        dlg.ShowDialog();
        StartBackgroundUpdateCheck();
    }

    private void StartBackgroundUpdateCheck()
    {
        if (!_settings.Current.AutoUpdateChecks) return;
        _ = Task.Run(async () =>
        {
            try { await _updates.CheckAndDownloadAsync(restart: false); }
            catch (Exception ex) { _log.Warning($"Background update check failed: {ex.Message}"); }
        });
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    private string _logFilterText = "";

    private void LogFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        _logFilterText = LogFilterBox.Text ?? "";
        var view = CollectionViewSource.GetDefaultView(_log.Entries);
        if (string.IsNullOrWhiteSpace(_logFilterText))
        {
            view.Filter = null;
        }
        else
        {
            var filter = _logFilterText;
            view.Filter = obj =>
                obj is LogEntry entry &&
                entry.Text.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }
    }
}
