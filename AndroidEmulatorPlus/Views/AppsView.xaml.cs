using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AndroidEmulatorPlus.ViewModels;

namespace AndroidEmulatorPlus.Views;

public partial class AppsView : UserControl
{
    public AppsView() => InitializeComponent();

    private static readonly string[] _exts = { ".apk", ".apks", ".xapk", ".apkm" };

    private static bool TryExtractApks(IDataObject data, out string[] files)
    {
        files = System.Array.Empty<string>();
        if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (data.GetData(DataFormats.FileDrop) is not string[] paths) return false;
        files = paths.Where(p => _exts.Contains(Path.GetExtension(p), System.StringComparer.OrdinalIgnoreCase)).ToArray();
        return files.Length > 0;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryExtractApks(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!TryExtractApks(e.Data, out var files)) return;
        if (DataContext is AppsViewModel vm) _ = vm.InstallApkFilesAsync(files);
        e.Handled = true;
    }
}
