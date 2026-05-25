using System.Collections.Specialized;
using System.Windows.Controls;

namespace AndroidEmulatorPlus.Views;

public partial class LogcatView : UserControl
{
    public LogcatView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookAutoScroll();
    }

    private INotifyCollectionChanged? _hooked;
    private void HookAutoScroll()
    {
        if (_hooked is not null) _hooked.CollectionChanged -= OnLinesChanged;
        if (DataContext is ViewModels.LogcatViewModel vm)
        {
            _hooked = vm.Lines;
            _hooked.CollectionChanged += OnLinesChanged;
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (LinesList.Items.Count > 0)
                LinesList.ScrollIntoView(LinesList.Items[LinesList.Items.Count - 1]);
        });
    }
}
