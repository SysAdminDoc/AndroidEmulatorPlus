using System.Windows;
using AndroidEmulatorPlus.Services;
using AndroidEmulatorPlus.ViewModels;

namespace AndroidEmulatorPlus.Views;

public partial class WelcomeDialog : Window
{
    private readonly MainViewModel _main;
    private readonly SettingsService _settings;

    private bool _sdkComplete;
    private bool _avdComplete;

    public WelcomeDialog(MainViewModel main, SettingsService settings, SdkLocator sdk, AvdService avds)
    {
        _main = main;
        _settings = settings;
        InitializeComponent();
        _sdkComplete = sdk.IsReady;
        SdkStatus.Text = _sdkComplete ? "✓ SDK detected." : "⚠ SDK not detected — start here.";
        var avdCount = avds.List().Count;
        _avdComplete = avdCount > 0;
        AvdStatus.Text = _avdComplete ? $"✓ {avdCount} AVD(s) on disk." : "⚠ No AVDs yet — create one.";
        ApplyVisibility();
    }

    /// <summary>C-14: hide step cards that are already done so the wizard focuses on what's next.</summary>
    private void ApplyVisibility()
    {
        var showAll = ShowAllBox?.IsChecked == true;
        SdkCard.Visibility = (_sdkComplete && !showAll) ? Visibility.Collapsed : Visibility.Visible;
        AvdCard.Visibility = (_avdComplete && !showAll) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowAll_Toggled(object sender, RoutedEventArgs e) => ApplyVisibility();

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
