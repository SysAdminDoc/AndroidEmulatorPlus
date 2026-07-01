using System.Windows;
using AndroidEmulatorPlus.Services;

namespace AndroidEmulatorPlus.Views;

public partial class LaunchOptionsDialog : Window
{
    public EmulatorService.LaunchOptions? Result { get; private set; }

    public LaunchOptionsDialog() => InitializeComponent();

    public static EmulatorService.LaunchOptions? Show(Window? owner, string avdName)
    {
        var dlg = new LaunchOptionsDialog
        {
            Owner = owner ?? Application.Current?.MainWindow,
        };
        dlg.HeaderText.Text = $"Launch options for '{avdName}'";
        var ok = dlg.ShowDialog() == true;
        return ok ? dlg.Result : null;
    }

    private static string? NormalizeCombo(System.Windows.Controls.ComboBox box)
    {
        var s = box.Text?.Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        Result = new EmulatorService.LaunchOptions(
            ColdBoot: ColdBootBox.IsChecked == true,
            WipeData: WipeBox.IsChecked == true,
            NoWindow: NoWindowBox.IsChecked == true,
            NoAudio: NoAudioBox.IsChecked == true,
            MultiDisplay: MultiDisplayBox.IsChecked == true,
            PeerNetworking: PeerNetworkBox.IsChecked == true,
            HttpProxy: string.IsNullOrWhiteSpace(HttpProxyBox.Text) ? null : HttpProxyBox.Text.Trim(),
            DnsServer: string.IsNullOrWhiteSpace(DnsBox.Text) ? null : DnsBox.Text.Trim(),
            FrontCamera: NormalizeCombo(FrontCameraBox),
            BackCamera: NormalizeCombo(BackCameraBox));
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
