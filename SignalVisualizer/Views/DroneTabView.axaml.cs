using System;
using System.Diagnostics;
using Avalonia.Controls;
using SignalVisualizer.ViewModels;

namespace SignalVisualizer.Views;

public partial class DroneTabView : UserControl
{
    private Action? _updateHandler;
    private DroneTabViewModel? _currentVm;

    public DroneTabView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Detach();

        if (DataContext is DroneTabViewModel vm)
        {
            _currentVm = vm;

            SignalPlot.Plot.Clear();
            var signal = SignalPlot.Plot.Add.Signal(vm.DataBuffer);
            signal.Data.Period = 1.0 / 100;

            // Show existing buffer immediately
            SignalPlot.Plot.Axes.AutoScale();
            SignalPlot.Refresh();

            // Throttle refreshes to max ~30 fps to prevent ScottPlot from choking
            var lastRender = Stopwatch.GetTimestamp();
            _updateHandler = () =>
            {
                var now = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(lastRender, now).TotalMilliseconds < 33)
                    return;
                lastRender = now;

                SignalPlot.Plot.Axes.AutoScale();
                SignalPlot.Refresh();
            };
            vm.DataUpdated += _updateHandler;
        }
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Detach();
    }

    private void Detach()
    {
        if (_currentVm != null && _updateHandler != null)
        {
            _currentVm.DataUpdated -= _updateHandler;
            _updateHandler = null;
            _currentVm = null;
        }
    }
}
