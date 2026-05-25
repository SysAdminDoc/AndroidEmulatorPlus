using System.Windows;
using System.Windows.Controls;

namespace AndroidEmulatorPlus.Views;

/// <summary>
/// Reusable destructive-action confirmation dialog with optional typed-confirm field.
/// Returns true via <c>ShowDialog()</c> if the user confirmed.
/// </summary>
public partial class ConfirmDialog : Window
{
    private string? _requiredText;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Build and show a confirmation dialog.
    /// </summary>
    /// <param name="owner">Parent window (for modal placement).</param>
    /// <param name="header">Headline (red, large).</param>
    /// <param name="body">Body explanation.</param>
    /// <param name="detail">Optional monospaced detail block (e.g. list of files about to be deleted).</param>
    /// <param name="confirmButtonText">Text on the destructive button. Default "Confirm".</param>
    /// <param name="typedConfirm">If set, the user must type this exact string before Confirm enables.</param>
    public static bool Show(Window? owner, string header, string body,
        string? detail = null, string confirmButtonText = "Confirm", string? typedConfirm = null)
    {
        var dlg = new ConfirmDialog
        {
            Owner = owner ?? Application.Current?.MainWindow,
        };
        dlg.HeaderText.Text = header;
        dlg.BodyText.Text = body;
        if (!string.IsNullOrEmpty(detail))
        {
            dlg.DetailText.Text = detail;
            dlg.DetailContainer.Visibility = Visibility.Visible;
        }
        dlg.OkButton.Content = confirmButtonText;
        if (!string.IsNullOrEmpty(typedConfirm))
        {
            dlg._requiredText = typedConfirm;
            dlg.TypedConfirmPrompt.Text = $"Type {typedConfirm} to confirm:";
            dlg.TypedConfirmPanel.Visibility = Visibility.Visible;
            dlg.OkButton.IsEnabled = false;
        }
        return dlg.ShowDialog() == true;
    }

    private void TypedConfirmBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_requiredText is null) return;
        OkButton.IsEnabled = string.Equals(TypedConfirmBox.Text, _requiredText, System.StringComparison.Ordinal);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void CancelButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
