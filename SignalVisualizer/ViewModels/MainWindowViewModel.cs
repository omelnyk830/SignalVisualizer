using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SignalVisualizer.Services;
using SignalVisualizer.Views;

namespace SignalVisualizer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public static FuncValueConverter<bool, string> PauseConverter { get; } =
        new(paused => paused ? "Resume" : "Pause");

    public static FuncValueConverter<bool, Color> ConnectionColorConverter { get; } =
        new(connected => connected ? Color.Parse("#2E7D32") : Color.Parse("#888888"));

    private readonly ISignalProcessor _processor;
    private readonly ICommandSource? _commandSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private IDisposable? _subscription;
    private IDisposable? _connectionSub;

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
    private string _connectionStatus = "No device";

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void SendSos() => _commandSource?.SendCommand("MODE_SOS");

    [RelayCommand]
    private void SendStandby() => _commandSource?.SendCommand("MODE_STB");

    [RelayCommand]
    private void SendStatus() => _commandSource?.SendCommand("STATUS");

    public bool HasCommandSource => _commandSource != null;

    [RelayCommand]
    private void OpenPacketLog()
    {
        // Each window gets its own scope — disposed when window closes
        var scope = _scopeFactory.CreateScope();
        var vm = scope.ServiceProvider.GetRequiredService<PacketLogViewModel>();

        var window = new PacketLogWindow
        {
            DataContext = vm,
        };

        // Dispose scope (and its services) when window closes
        window.Closed += (_, _) => scope.Dispose();
        window.Show();
    }

    public MainWindowViewModel(
        ISignalProcessor processor,
        IServiceScopeFactory scopeFactory,
        ICommandSource? commandSource = null,
        MavlinkSignalSource? mavlink = null)
    {
        _processor = processor;
        _scopeFactory = scopeFactory;
        _commandSource = commandSource;

        if (mavlink != null)
        {
            _connectionSub = mavlink.ConnectionState
                .Subscribe(connected =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsConnected = connected;
                        ConnectionStatus = connected ? "Connected" : "Waiting for device…";
                    });
                });
        }

        _subscription = _processor.ProcessedStream
            .Subscribe(batch =>
            {
                // Marshal everything to UI thread — single writer, no locks needed
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

    public MainWindowViewModel()
    {
        _processor = null!;
        _scopeFactory = null!;
    }

    public void Dispose()
    {
        _processor?.Stop();
        _subscription?.Dispose();
        _connectionSub?.Dispose();
    }
}