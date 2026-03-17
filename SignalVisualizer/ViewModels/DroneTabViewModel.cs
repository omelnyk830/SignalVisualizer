using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SignalVisualizer.Models;
using SignalVisualizer.Services;

namespace SignalVisualizer.ViewModels;

/// <summary>
/// ViewModel for a single drone tab. Each connected UAV gets one of these.
/// Owns its own data buffer, stats, and subscription to the drone's telemetry.
/// </summary>
public partial class DroneTabViewModel : ViewModelBase, IDisposable
{

    private readonly DroneSession _session;
    private readonly SignalProcessor _processor;
    private IDisposable? _dataSub;
    private IDisposable? _connectionSub;

    public string DroneId => _session.DroneId;
    public string PortName => _session.PortName;
    public string TabHeader => $"{_session.DroneId} ({_session.PortName})";

    public double[] DataBuffer { get; } = new double[1000];
    private int _writeIndex;

    public event Action? DataUpdated;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private int _packetCount;

    [ObservableProperty]
    private double _lastValue;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Connecting…";

    public bool HasCommandSource => _session.CommandSource != null;

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void SendSos() => _session.CommandSource?.SendCommand("MODE_SOS");

    [RelayCommand]
    private void SendStandby() => _session.CommandSource?.SendCommand("MODE_STB");

    [RelayCommand]
    private void SendStatus() => _session.CommandSource?.SendCommand("STATUS");

    public DroneTabViewModel(DroneSession session)
    {
        _session = session;

        // Each drone gets its own processor for batching
        _processor = new SignalProcessor(new ObservableSignalSource(session.ImuStream));

        _connectionSub = session.ConnectionState
            .Subscribe(connected =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsConnected = connected;
                    ConnectionStatus = connected ? "Connected" : "Reconnecting…";
                });
            });

        _dataSub = _processor.ProcessedStream
            .Subscribe(batch =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsPaused) return;

                    foreach (var sample in batch)
                    {
                        DataBuffer[_writeIndex % DataBuffer.Length] = sample;
                        _writeIndex++;
                        PacketCount++;
                        LastValue = sample;
                    }

                    DataUpdated?.Invoke();
                });
            });

        _processor.Start();
    }

    // Design-time constructor
    public DroneTabViewModel()
    {
        _session = null!;
        _processor = null!;
    }

    public void Dispose()
    {
        _processor?.Stop();
        _dataSub?.Dispose();
        _connectionSub?.Dispose();
        _processor?.Dispose();
    }

    /// <summary>
    /// Lightweight adapter: wraps an IObservable&lt;double&gt; as an ISignalSource
    /// so SignalProcessor can consume DroneSession.ImuStream without coupling.
    /// </summary>
    private sealed class ObservableSignalSource : ISignalSource
    {
        public IObservable<double> SignalStream { get; }
        public ObservableSignalSource(IObservable<double> stream) => SignalStream = stream;
        public void Start() { }
        public void Stop() { }
    }
}