using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SignalVisualizer.Services;

/// <summary>
/// Listens on a UDP discovery port for drone announcements.
/// Drones send: "HELLO:{droneId}:{dataPort}" to announce themselves.
/// Ground station then creates a UdpSignalSource on that data port.
/// </summary>
public sealed class UdpDroneDiscovery : IDisposable
{
    public const int DefaultDiscoveryPort = 14550;

    private readonly int _discoveryPort;
    private readonly Subject<DroneAnnouncement> _announcements = new();
    private UdpClient? _client;
    private CancellationTokenSource? _cts;

    public IObservable<DroneAnnouncement> Announcements => _announcements.AsObservable();

    public UdpDroneDiscovery(int discoveryPort = DefaultDiscoveryPort)
    {
        _discoveryPort = discoveryPort;
    }

    public void Start()
    {
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, _discoveryPort));
        _cts = new CancellationTokenSource();
        Task.Run(() => ListenLoop(_cts.Token));
        Console.WriteLine($"[Discovery] Listening on UDP :{_discoveryPort}");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _client?.Dispose();
        _client = null;
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _client!.ReceiveAsync(ct);
                var msg = Encoding.UTF8.GetString(result.Buffer);

                // Protocol: "HELLO:{droneId}:{dataPort}"
                var parts = msg.Split(':');
                if (parts.Length >= 3
                    && parts[0] == "HELLO"
                    && int.TryParse(parts[2], out int dataPort))
                {
                    _announcements.OnNext(new DroneAnnouncement(
                        parts[1],
                        result.RemoteEndPoint.Address,
                        dataPort));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _announcements.Dispose();
    }
}

public readonly record struct DroneAnnouncement(string DroneId, IPAddress Address, int DataPort);
