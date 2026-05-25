using System.Windows;
using AndroidEmulatorPlus.Services;

namespace AndroidEmulatorPlus.Views;

public partial class SnapshotDialog : Window
{
    private readonly SnapshotService _svc;
    private readonly string _avdName;
    private readonly string? _serial;

    public SnapshotDialog(SnapshotService svc, string avdName, string? runningSerial)
    {
        _svc = svc;
        _avdName = avdName;
        _serial = runningSerial;
        InitializeComponent();
        HeaderText.Text = $"Snapshots for '{avdName}'";
        Refresh();
        UpdateStatus();
    }

    private void Refresh()
    {
        List.ItemsSource = _svc.List(_avdName);
    }

    private void UpdateStatus()
    {
        if (_serial is null) StatusText.Text = "AVD is not running — Save/Load disabled.";
        else StatusText.Text = $"Connected via {_serial}.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_serial is null) { StatusText.Text = "Launch the AVD first."; return; }
        var name = (SaveNameBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name)) { StatusText.Text = "Enter a snapshot name."; return; }
        if (!SnapshotService.IsSafeSnapshotName(name))
        {
            StatusText.Text = "Snapshot names may contain only letters, digits, spaces, '.', '_' and '-'.";
            return;
        }
        StatusText.Text = $"Saving snapshot '{name}'…";
        var ok = await _svc.SaveAsync(_serial, name);
        StatusText.Text = ok ? $"✓ Snapshot '{name}' saved." : "✗ Save failed — see log.";
        if (ok) Refresh();
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        if (_serial is null) { StatusText.Text = "Launch the AVD first."; return; }
        if (List.SelectedItem is not Snapshot snap) { StatusText.Text = "Select a snapshot first."; return; }
        StatusText.Text = $"Loading '{snap.Name}'…";
        var ok = await _svc.LoadAsync(_serial, snap.Name);
        StatusText.Text = ok ? $"✓ Loaded '{snap.Name}'." : "✗ Load failed — see log.";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not Snapshot snap) { StatusText.Text = "Select a snapshot first."; return; }
        var ok = ConfirmDialog.Show(
            owner: this,
            header: $"Delete snapshot '{snap.Name}'?",
            body: "This deletes the snapshot folder. The AVD continues running; only the saved state is removed.",
            detail: $"Folder: {snap.Folder}\nSize:   {snap.SizeBytes / 1024 / 1024} MB",
            confirmButtonText: "Delete snapshot");
        if (!ok) return;
        if (_svc.Delete(_avdName, snap.Name)) Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
