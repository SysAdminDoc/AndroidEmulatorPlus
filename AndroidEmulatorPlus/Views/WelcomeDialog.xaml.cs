using System.Windows;
using AndroidEmulatorPlus.Services;
using AndroidEmulatorPlus.ViewModels;

namespace AndroidEmulatorPlus.Views;

public partial class WelcomeDialog : Window
{
    private readonly MainViewModel _main;
    private readonly SettingsService _settings;

    public WelcomeDialog(MainViewModel main, SettingsService settings, SdkLocator sdk, AvdService avds)
    {
        _main = main;
        _settings = settings;
        InitializeComponent();
        SdkStatus.Text = sdk.IsReady ? "✓ SDK detected." : "⚠ SDK not detected — start here.";
        var avdCount = avds.List().Count;
        AvdStatus.Text = avdCount > 0 ? $"✓ {avdCount} AVD(s) on disk." : "⚠ No AVDs yet — create one.";
    }

    private void Navigate(string section)
    {
        _main.NavigateCommand.Execute(section);
        Close();
    }

    private void GoToInstall_Click(object sender, RoutedEventArgs e) => Navigate("Install");
    private void GoToAvd_Click(object sender, RoutedEventArgs e) => Navigate("Avd");
    private void GoToRoot_Click(object sender, RoutedEventArgs e) => Navigate("Root");
    private void GoToMigrate_Click(object sender, RoutedEventArgs e) => Navigate("Migrate");

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DontShowBox.IsChecked == true)
        {
            _settings.Current.HasSeenWizard = true;
            _settings.Save();
        }
        Close();
    }
}
