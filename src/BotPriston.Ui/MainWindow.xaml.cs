using System.Collections.Specialized;
using System.Windows;

namespace BotPriston.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.LogLines.CollectionChanged += ScrollLogToEnd;
        };
    }

    /// <summary>Keep the newest log line in view.</summary>
    private void ScrollLogToEnd(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && Log.Items.Count > 0)
            Log.ScrollIntoView(Log.Items[^1]);
    }
}
