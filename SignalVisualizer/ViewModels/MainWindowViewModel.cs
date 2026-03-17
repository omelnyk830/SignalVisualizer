using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SignalVisualizer.Models;
using SignalVisualizer.Services;

namespace SignalVisualizer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly DroneManager _droneManager;
    private IDisposable? _eventSub;

    public ObservableCollection<DroneTabViewModel> Drones { get; } = new();

    [ObservableProperty]
    private DroneTabViewModel? _selectedDrone;

    [ObservableProperty]
    private int _droneCount;

    public MainWindowViewModel(DroneManager droneManager)
    {
        _droneManager = droneManager;

        _eventSub = _droneManager.Events
            .Subscribe(ev =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    switch (ev.Type)
                    {
                        case DroneEventType.Connected:
                            var tab = new DroneTabViewModel(ev.Session);
                            Drones.Add(tab);
                            SelectedDrone ??= tab;
                            DroneCount = Drones.Count;
                            break;

                        case DroneEventType.Disconnected:
                            var existing = Drones.FirstOrDefault(d => d.DroneId == ev.Session.DroneId);
                            if (existing != null)
                            {
                                Drones.Remove(existing);
                                existing.Dispose();
                                if (SelectedDrone == existing)
                                    SelectedDrone = Drones.FirstOrDefault();
                                DroneCount = Drones.Count;
                            }
                            break;
                    }
                });
            });

        _droneManager.Start();
    }

    // Design-time constructor
    public MainWindowViewModel()
    {
        _droneManager = null!;
    }

    public void Dispose()
    {
        _droneManager?.Stop();
        _eventSub?.Dispose();
        foreach (var drone in Drones)
            drone.Dispose();
    }
}