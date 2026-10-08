using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;

namespace BotPriston.Ui;

public partial class MainWindow : Window
{
    private bool _scrollPending;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.LogLines.CollectionChanged += ScrollLogToEnd;
        };
    }

    /// <summary>
    /// Keep the newest log line in view. Scrolling must wait until the list has processed the change:
    /// doing it inside the CollectionChanged notification makes WPF see an inconsistent list and throw.
    /// Lines often arrive in bursts, so one scroll per burst.
    /// </summary>
    private void ScrollLogToEnd(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _scrollPending) return;
        _scrollPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scrollPending = false;
            if (Log.Items.Count > 0)
                Log.ScrollIntoView(Log.Items[^1]);
        });
    }
}
