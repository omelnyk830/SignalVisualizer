using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SignalVisualizer.Services;

namespace SignalVisualizer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public static FuncValueConverter<bool, string> PauseConverter { get; } =
        new(paused => paused ? "Resume" : "Pause");

    private readonly ISignalProcessor _processor;
    private readonly ICommandSource? _commandSource;
    private IDisposable? _subscription;

    public double[] DataBuffer { get; } = new double[1000];
    private int _writeIndex;

    public event Action? DataUpdated;

    // Packet log
    public ObservableCollection<string> PacketLog { get; } = new();
    private const int MaxLogEntries = 200;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private int _packetCount;

    [ObservableProperty]
    private double _lastValue;

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
    }

    [RelayCommand]
    private void ClearLog()
    {
        PacketLog.Clear();
        PacketCount = 0;
    }

    [RelayCommand]
    private void SendSos() => _commandSource?.SendCommand("MODE_SOS");

    [RelayCommand]
    private void SendStandby() => _commandSource?.SendCommand("MODE_STB");

    [RelayCommand]
    private void SendStatus() => _commandSource?.SendCommand("STATUS");

    public bool HasCommandSource => _commandSource != null;

    public MainWindowViewModel(ISignalProcessor processor, ICommandSource? commandSource = null)
    {
        _processor = processor;
        _commandSource = commandSource;

        _subscription = _processor.ProcessedStream
            .Subscribe(batch =>
            {
                if (IsPaused) return;

                foreach (var sample in batch)
                {
                    DataBuffer[_writeIndex % DataBuffer.Length] = sample;
                    _writeIndex++;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (IsPaused) return;

                    foreach (var sample in batch)
                    {
                        PacketCount++;
                        LastValue = sample;

                        var raw = (ushort)sample;
                        var hi = (byte)(raw >> 8);
                        var lo = (byte)(raw & 0xFF);
                        var chk = (byte)(0xAA ^ hi ^ lo);
                        var voltage = sample * 3.3 / 4096;
                        var temp = ((1.43 - voltage) / 0.0043) + 25;
                        var line = $"#{PacketCount,6} | [AA {hi:X2} {lo:X2} {chk:X2}] | ADC={raw,4} | {voltage:F3}V | {temp:F1}°C";

                        PacketLog.Add(line);
                        if (PacketLog.Count > MaxLogEntries)
                            PacketLog.RemoveAt(0);
                    }

                    DataUpdated?.Invoke();
                });
            });

        _processor.Start();
    }

    public MainWindowViewModel()
    {
        // Design-time only
        _processor = null!;
    }

    public void Dispose()
    {
        _processor?.Stop();
        _subscription?.Dispose();
    }
}