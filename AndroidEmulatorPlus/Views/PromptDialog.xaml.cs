using System.Windows;
using System.Windows.Input;

namespace AndroidEmulatorPlus.Views;

/// <summary>Single-line text prompt with optional validation predicate.</summary>
public partial class PromptDialog : Window
{
    private System.Func<string, string?>? _validate;

    public PromptDialog() => InitializeComponent();

    /// <summary>
    /// Show a modal prompt. Returns the entered text if confirmed, null if cancelled.
    /// </summary>
    /// <param name="validate">Optional predicate that returns an error message
    /// when the input is invalid, or null when it's ok.</param>
    public static string? Show(Window? owner, string header, string body,
        string initial = "", string okText = "OK",
        System.Func<string, string?>? validate = null)
    {
        var dlg = new PromptDialog
        {
            Owner = owner ?? Application.Current?.MainWindow,
            _validate = validate,
        };
        dlg.HeaderText.Text = header;
        dlg.BodyText.Text = body;
        dlg.InputBox.Text = initial;
        dlg.OkButton.Content = okText;
        dlg.InputBox.Focus();
        dlg.InputBox.SelectAll();
        return dlg.ShowDialog() == true ? dlg.InputBox.Text : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_validate is not null)
        {
            var err = _validate(InputBox.Text);
            if (err is not null)
            {
                ErrorText.Text = err;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
        }
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OkButton_Click(sender, e);
    }
}
