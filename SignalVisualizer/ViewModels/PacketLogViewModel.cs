using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SignalVisualizer.Services;

namespace SignalVisualizer.ViewModels;

public partial class PacketLogViewModel : ViewModelBase, IDisposable
{
    private readonly ISignalProcessor _processor;
    private IDisposable? _subscription;

    public ObservableCollection<string> PacketLog { get; } = new();
    private const int MaxLogEntries = 200;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private int _packetCount;

    [ObservableProperty]
    private double _lastValue;

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void ClearLog()
    {
        PacketLog.Clear();
        PacketCount = 0;
    }

    public PacketLogViewModel(ISignalProcessor processor)
    {
        _processor = processor;

        _subscription = _processor.ProcessedStream
            .Subscribe(batch =>
            {
                if (IsPaused) return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (IsPaused) return;

                    foreach (var sample in batch)
                    {
                        PacketCount++;
                        LastValue = sample;

                        var raw = (short)sample;
                        var voltage = Math.Abs(sample) * 3.3 / 4096;
                        var temp = ((1.43 - voltage) / 0.0043) + 25;
                        var line = $"#{PacketCount,6} | RAW_IMU xacc={raw,5} | {voltage:F3}V | {temp:F1}°C";

                        PacketLog.Add(line);
                        if (PacketLog.Count > MaxLogEntries)
                            PacketLog.RemoveAt(0);
                    }
                });
            });
    }

    public PacketLogViewModel()
    {
        _processor = null!;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}