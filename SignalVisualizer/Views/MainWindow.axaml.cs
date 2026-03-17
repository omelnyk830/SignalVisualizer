using System;
using Avalonia.Controls;
using SignalVisualizer.ViewModels;

namespace SignalVisualizer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            var signal = SignalPlot.Plot.Add.Signal(vm.DataBuffer);
            signal.Data.Period = 1.0 / 100;

            vm.DataUpdated += () =>
            {
                SignalPlot.Plot.Axes.AutoScale();
                SignalPlot.Refresh();

                // Auto-scroll packet log to bottom
                if (PacketLogList.ItemCount > 0 && PacketLogList.IsLoaded)
                    try { PacketLogList.ScrollIntoView(PacketLogList.ItemCount - 1); } catch { }
            };
        }
    }
}