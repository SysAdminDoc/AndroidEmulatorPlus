using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AndroidEmulatorPlus.Services;

namespace AndroidEmulatorPlus.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsService _settings;
    private readonly UpdateService _updates;

    public SettingsDialog(SettingsService settings, UpdateService updates)
    {
        _settings = settings;
        _updates = updates;
        InitializeComponent();
        // Bind the current values into controls (without two-way binding so Cancel is meaningful).
        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if (string.Equals(item.Content?.ToString(), _settings.Current.Theme, System.StringComparison.OrdinalIgnoreCase))
                ThemeBox.SelectedItem = item;
        }
        SdkRootBox.Text = _settings.Current.SdkRootOverride ?? "";
        MediaDirBox.Text = _settings.Current.MediaDir ?? "";
        ProxyBox.Text = _settings.Current.HttpProxy ?? "";
        AutoScrcpyBox.IsChecked = _settings.Current.AutoScrcpy;
        AutoUpdatesBox.IsChecked = _settings.Current.AutoUpdateChecks;
    }

    private void BrowseSdk_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pick SDK root", Multiselect = false };
        if (dlg.ShowDialog() == true) SdkRootBox.Text = dlg.FolderName;
    }

    private void BrowseMedia_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pick media output dir", Multiselect = false };
        if (dlg.ShowDialog() == true) MediaDirBox.Text = dlg.FolderName;
    }

    private void OpenJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsService.Path)!);
            if (!File.Exists(SettingsService.Path)) _settings.Save();
            Process.Start(new ProcessStartInfo(SettingsService.Path) { UseShellExecute = true });
        }
        catch { }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var newTheme = (ThemeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "mocha";
        var themeChanged = !string.Equals(newTheme, _settings.Current.Theme, System.StringComparison.OrdinalIgnoreCase);
        var proxy = string.IsNullOrWhiteSpace(ProxyBox.Text) ? null : ProxyBox.Text.Trim();
        if (proxy is not null
            && (!Uri.TryCreate(proxy, UriKind.Absolute, out var proxyUri)
                || (proxyUri.Scheme != Uri.UriSchemeHttp && proxyUri.Scheme != Uri.UriSchemeHttps)))
        {
            MessageBox.Show(this, "HTTP proxy must be an absolute http:// or https:// URL.", "Invalid proxy",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.Current.SdkRootOverride = string.IsNullOrWhiteSpace(SdkRootBox.Text) ? null : SdkRootBox.Text.Trim();
        _settings.Current.MediaDir = string.IsNullOrWhiteSpace(MediaDirBox.Text) ? null : MediaDirBox.Text.Trim();
        _settings.Current.HttpProxy = proxy;
        _settings.Current.AutoScrcpy = AutoScrcpyBox.IsChecked == true;
        _settings.Current.AutoUpdateChecks = AutoUpdatesBox.IsChecked == true;
        _settings.Save();
        // C-12: live theme swap via ThemeService (also persists the chosen theme).
        if (themeChanged && App.Services.GetService(typeof(ThemeService)) is ThemeService theme)
            theme.Apply(newTheme);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        => await RunUpdateCheckAsync(restart: false);

    private async void RestartForUpdate_Click(object sender, RoutedEventArgs e)
        => await RunUpdateCheckAsync(restart: true);

    private async Task RunUpdateCheckAsync(bool restart)
    {
        CheckUpdatesButton.IsEnabled = false;
        RestartForUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = restart
            ? "Checking for updates before restart..."
            : "Checking for updates...";
        try
        {
            var result = await _updates.CheckAndDownloadAsync(restart);
            UpdateStatusText.Text = result.Message;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            RestartForUpdateButton.IsEnabled = true;
        }
    }

    /// <summary>C-10: reopen the first-launch wizard from Settings.</summary>
    private void ShowWizard_Click(object sender, RoutedEventArgs e)
    {
        _settings.Current.HasSeenWizard = false;
        _settings.Save();
        // We don't construct the WelcomeDialog here because it needs DI'd services
        // (MainViewModel + SdkLocator + AvdService). Close this dialog with a
        // signal flag and let the owner window react in its Closed handler — we
        // piggy-back on DialogResult=true so the existing Save path is reused
        // to reload the SdkLocator on settings changes; the owner separately
        // checks HasSeenWizard and re-opens the wizard.
        DialogResult = true;
        Close();
    }
}
