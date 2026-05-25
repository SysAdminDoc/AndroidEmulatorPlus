using System.Windows;
using System.Windows.Controls;

namespace AndroidEmulatorPlus.Views;

public partial class AvdView : UserControl
{
    public AvdView() => InitializeComponent();

    private void OverflowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.ContextMenu is { } menu)
        {
            menu.PlacementTarget = b;
            menu.IsOpen = true;
        }
    }
}
