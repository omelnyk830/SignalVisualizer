using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using SignalVisualizer.Services;

namespace SignalVisualizer.Models;

/// <summary>
/// Represents a single connected UAV with all its telemetry streams.
/// Protocol-agnostic — works with any ISignalSource (MAVLink, raw serial, etc.)
/// Created by DroneManager when a new serial port appears, disposed on disconnect.
/// </summary>
public sealed class DroneSession : IDisposable
{
    private readonly ISignalSource _source;

    public string DroneId { get; }
    public string PortName { get; }

    // Telemetry channels
    public IObservable<double> ImuStream => _source.SignalStream;
    public BehaviorSubject<double> Battery { get; } = new(double.NaN);
    public BehaviorSubject<(double Lat, double Lon, double Alt)> Position { get; } = new((double.NaN, double.NaN, double.NaN));

    // Connection state — if source implements IConnectionAware, use it; otherwise assume connected
    public IObservable<bool> ConnectionState =>
        (_source as IConnectionAware)?.ConnectionState ?? Observable.Return(true);

    // Optional: command support for sources that implement ICommandSource
    public ICommandSource? CommandSource => _source as ICommandSource;

    public DroneSession(string droneId, string portName, ISignalSource source)
    {
        DroneId = droneId;
        PortName = portName;
        _source = source;
    }

    public void Start() => _source.Start();

    public void Dispose()
    {
        _source.Stop();
        Battery.Dispose();
        Position.Dispose();
        (_source as IDisposable)?.Dispose();
    }
}