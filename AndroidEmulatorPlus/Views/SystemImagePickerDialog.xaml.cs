using System.Windows;
using System.Windows.Controls;
using AndroidEmulatorPlus.Services;

namespace AndroidEmulatorPlus.Views;

/// <summary>
/// Browses `sdkmanager --list`, lets the user pick a system-images;… package, and runs
/// sdkmanager install with auto-license-accept. Returns the installed package name
/// via <see cref="InstalledPackage"/> when the dialog closes with DialogResult=true.
/// </summary>
public partial class SystemImagePickerDialog : Window
{
    private readonly SdkmanagerService _sdkman;
    private readonly ToastService? _toast;
    private List<string> _all = new();

    public string? InstalledPackage { get; private set; }

    public SystemImagePickerDialog(SdkmanagerService sdkman)
    {
        _sdkman = sdkman;
        _toast = App.Services.GetService(typeof(ToastService)) as ToastService;
        InitializeComponent();
        ImagesList.SelectionChanged += (_, _) => InstallButton.IsEnabled = ImagesList.SelectedItem is string;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = "Querying sdkmanager…";
        RefreshButton.IsEnabled = false;
        try
        {
            _all = await _sdkman.ListAvailableSystemImagesAsync();
            ApplyFilter();
            StatusText.Text = $"{_all.Count} package(s) available.";
        }
        catch (System.Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
        finally { RefreshButton.IsEnabled = true; }
    }

    private void ApplyFilter()
    {
        var f = (FilterBox.Text ?? "").Trim();
        var filtered = string.IsNullOrEmpty(f)
            ? _all
            : _all.Where(s => s.Contains(f, System.StringComparison.OrdinalIgnoreCase)).ToList();
        ImagesList.ItemsSource = filtered;
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Licenses_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        LicensesButton.IsEnabled = false;
        ProgressText.Text = "Accepting SDK licenses…";
        await _sdkman.AcceptLicensesAsync(new System.Progress<string>(s => ProgressText.Text = s));
        ProgressText.Text = "Licenses accepted.";
        LicensesButton.IsEnabled = true;
        InstallButton.IsEnabled = ImagesList.SelectedItem is string;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (ImagesList.SelectedItem is not string pkg) return;
        InstallButton.IsEnabled = false;
        LicensesButton.IsEnabled = false;
        InstallProgressBar.Visibility = Visibility.Visible;
        InstallProgressBar.IsIndeterminate = true;
        ProgressText.Text = $"Installing {pkg}… (this can take several minutes)";

        var progress = new System.Progress<(int percent, string status)>(update =>
        {
            InstallProgressBar.IsIndeterminate = false;
            InstallProgressBar.Value = update.percent;
            ProgressText.Text = $"Installing {pkg}… {update.percent}%  {update.status}";
        });

        var ok = await _sdkman.InstallWithProgressAsync(new[] { pkg }, progress);
        if (ok)
        {
            _toast?.Show("System image installed", pkg);
            InstalledPackage = pkg;
            DialogResult = true;
            Close();
        }
        else
        {
            ProgressText.Text = $"Install failed for {pkg}. Check the log panel.";
            InstallProgressBar.Visibility = Visibility.Collapsed;
            InstallButton.IsEnabled = true;
            LicensesButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
