using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using SignalVisualizer.Models;

namespace SignalVisualizer.Services;

/// <summary>
/// Manages drone sessions from multiple transports:
///   - Serial: scans USB ports on a timer
///   - UDP: reacts to discovery announcements
/// </summary>
public sealed class DroneManager : IDisposable
{
    private readonly string _portPattern;
    private readonly int _baudRate;
    private readonly ISignalSourceFactory _factory;
    private readonly UdpDroneDiscovery? _discovery;
    private readonly ConcurrentDictionary<string, DroneSession> _sessions = new();
    private readonly Subject<DroneEvent> _events = new();
    private Timer? _scanTimer;
    private IDisposable? _discoverySub;
    private int _droneCounter;

    public IObservable<DroneEvent> Events => _events.AsObservable();
    public IEnumerable<DroneSession> Sessions => _sessions.Values;

    public DroneManager(
        ISignalSourceFactory factory,
        UdpDroneDiscovery? discovery = null,
        string portPattern = "usbmodem",
        int baudRate = 115200)
    {
        _factory = factory;
        _discovery = discovery;
        _portPattern = portPattern;
        _baudRate = baudRate;
    }

    public void Start()
    {
        // Serial port scanning
        _scanTimer = new Timer(_ => ScanPorts(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        // UDP discovery
        if (_discovery != null)
        {
            _discovery.Start();
            _discoverySub = _discovery.Announcements
                .Subscribe(OnDroneAnnouncement);
        }
    }

    public void Stop()
    {
        _scanTimer?.Dispose();
        _scanTimer = null;
        _discoverySub?.Dispose();
        _discovery?.Stop();

        foreach (var kvp in _sessions)
        {
            if (_sessions.TryRemove(kvp.Key, out var session))
            {
                _events.OnNext(new DroneEvent(DroneEventType.Disconnected, session));
                session.Dispose();
            }
        }
    }

    private void OnDroneAnnouncement(DroneAnnouncement ann)
    {
        // Key by "udp:{droneId}" to avoid collision with serial port names
        var key = $"udp:{ann.DroneId}";
        if (_sessions.ContainsKey(key))
            return;

        var source = new UdpSignalSource(ann.DataPort);
        var session = new DroneSession(ann.DroneId, $"{ann.Address}:{ann.DataPort}", source);

        if (_sessions.TryAdd(key, session))
        {
            session.Start();
            _events.OnNext(new DroneEvent(DroneEventType.Connected, session));
            Console.WriteLine($"[DroneManager] {ann.DroneId} discovered via UDP on port {ann.DataPort}");
        }
    }

    private void ScanPorts()
    {
        var activePorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var portName in SerialPort.GetPortNames())
        {
            if (!portName.Contains(_portPattern, StringComparison.OrdinalIgnoreCase))
                continue;

            activePorts.Add(portName);

            if (_sessions.ContainsKey(portName))
                continue;

            var id = $"UAV-{Interlocked.Increment(ref _droneCounter)}";
            var source = _factory.Create(portName, _baudRate);
            var session = new DroneSession(id, portName, source);

            if (_sessions.TryAdd(portName, session))
            {
                session.Start();
                _events.OnNext(new DroneEvent(DroneEventType.Connected, session));
                Console.WriteLine($"[DroneManager] {id} connected on {portName}");
            }
        }

        // Only remove serial sessions (UDP sessions manage their own lifecycle via watchdog)
        foreach (var kvp in _sessions)
        {
            if (kvp.Key.StartsWith("udp:"))
                continue;

            if (activePorts.Contains(kvp.Key))
                continue;

            if (_sessions.TryRemove(kvp.Key, out var session))
            {
                _events.OnNext(new DroneEvent(DroneEventType.Disconnected, session));
                session.Dispose();
                Console.WriteLine($"[DroneManager] {session.DroneId} disconnected from {kvp.Key}");
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _events.Dispose();
    }
}
