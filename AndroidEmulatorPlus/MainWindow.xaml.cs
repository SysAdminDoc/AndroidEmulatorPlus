using System.Windows;
using System.Windows.Controls;
using AndroidEmulatorPlus.Services;
using AndroidEmulatorPlus.ViewModels;

namespace AndroidEmulatorPlus;

public partial class MainWindow : Window
{
    private readonly LogService _log;

    public MainWindow(LogService log, MainViewModel vm)
    {
        _log = log;
        InitializeComponent();
        DataContext = vm;
        // Auto-scroll the log to the bottom as entries arrive.
        log.EntryAdded += _ =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (LogList.Template.FindName("LogScroll", LogList) is ScrollViewer sv)
                    sv.ScrollToEnd();
            });
        };
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();
}
