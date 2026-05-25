using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AndroidEmulatorPlus.Services;

namespace AndroidEmulatorPlus.Views;

public partial class MagiskModulesDialog : Window
{
    private readonly MagiskService _svc;
    private readonly string _serial;
    private MagiskModuleCatalogEntry? _selected;

    public MagiskModulesDialog(MagiskService svc, string serial)
    {
        _svc = svc;
        _serial = serial;
        InitializeComponent();
        TargetText.Text = $"Connected to {serial}. Module changes need a cold-boot of the emulator to take effect.";
        CatalogList.ItemsSource = _svc.Catalog;
        Loaded += async (_, _) => await RefreshInstalledAsync();
    }

    private async Task RefreshInstalledAsync()
    {
        StatusText.Text = "Querying magisk module list…";
        try
        {
            var list = await _svc.ListInstalledAsync(_serial);
            InstalledList.ItemsSource = list;
            StatusText.Text = list.Count == 0
                ? "No modules installed."
                : $"{list.Count} module(s) installed.";
        }
        catch (System.Exception ex) { StatusText.Text = "Error: " + ex.Message; }
    }

    private void CatalogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _selected = CatalogList.SelectedItem as MagiskModuleCatalogEntry;

    private void OpenHomepage_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Homepage is null) { StatusText.Text = "Select a module first."; return; }
        try { Process.Start(new ProcessStartInfo(_selected.Homepage) { UseShellExecute = true }); }
        catch (System.Exception ex) { StatusText.Text = "Open failed: " + ex.Message; }
    }

    private async void InstallCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { StatusText.Text = "Select a module first."; return; }
        StatusText.Text = $"Installing {_selected.Name}…";
        var ok = await _svc.InstallCatalogEntryAsync(_serial, _selected);
        StatusText.Text = ok ? $"✓ {_selected.Name} installed. Cold-boot the emulator." : "✗ Install failed — see log.";
        if (ok) await RefreshInstalledAsync();
    }

    private async void InstallFromZip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Magisk module (*.zip)|*.zip|All files|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        StatusText.Text = $"Installing {Path.GetFileName(dlg.FileName)}…";
        var ok = await _svc.InstallFromZipAsync(_serial, dlg.FileName);
        StatusText.Text = ok ? "✓ Installed. Cold-boot the emulator." : "✗ Install failed — see log.";
        if (ok) await RefreshInstalledAsync();
    }

    private async void RefreshInstalled_Click(object sender, RoutedEventArgs e) => await RefreshInstalledAsync();

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledList.SelectedItem is not InstalledMagiskModule m) { StatusText.Text = "Select an installed module first."; return; }
        StatusText.Text = $"{(m.Enabled ? "Disabling" : "Enabling")} {m.Id}…";
        var ok = await _svc.SetEnabledAsync(_serial, m.Id, !m.Enabled);
        StatusText.Text = ok ? $"✓ Toggled. Cold-boot to apply." : "✗ Toggle failed — see log.";
        if (ok) await RefreshInstalledAsync();
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledList.SelectedItem is not InstalledMagiskModule m) { StatusText.Text = "Select an installed module first."; return; }
        var ok = ConfirmDialog.Show(this,
            header: $"Remove module '{m.Name}'?",
            body: "Marks the module for removal. The actual delete happens on the next cold-boot of the emulator.",
            detail: $"id:      {m.Id}\nversion: {m.Version}",
            confirmButtonText: "Mark for removal");
        if (!ok) return;
        await _svc.RemoveAsync(_serial, m.Id);
        await RefreshInstalledAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
