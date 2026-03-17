using System;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace SignalVisualizer.Services;

/// <summary>
/// Reads MAVLink v2 frames from a UDP socket.
/// Each drone sends to a unique port on the ground station.
/// </summary>
public sealed class UdpSignalSource : ISignalSource, IConnectionAware, IDisposable
{
    private const byte MavlinkStx = 0xFD;
    private const int RawImuMsgId = 27;
    private const int HeaderLen = 9;
    private const int CrcLen = 2;

    private readonly IPEndPoint _listenEndpoint;
    private readonly Subject<double> _subject = new();
    private readonly BehaviorSubject<bool> _connected = new(false);
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private long _lastReceivedTicks;
    private long _totalPackets;

    public int Port => _listenEndpoint.Port;
    public IObservable<double> SignalStream => _subject.AsObservable();
    public IObservable<bool> ConnectionState => _connected.AsObservable();

    public IPEndPoint? RemoteEndpoint { get; private set; }

    public UdpSignalSource(int listenPort)
    {
        _listenEndpoint = new IPEndPoint(IPAddress.Any, listenPort);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _client = new UdpClient(_listenEndpoint);
        Console.WriteLine($"[UDP:{Port}] Listening");
        Task.Run(() => ReceiveLoop(_cts.Token));
        Task.Run(() => HeartbeatWatchdog(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _client?.Dispose();
        _client = null;
        _connected.OnNext(false);
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var client = _client;
        if (client == null) return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(ct);
                RemoteEndpoint = result.RemoteEndPoint;
                Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);

                if (!_connected.Value)
                {
                    _connected.OnNext(true);
                    Console.WriteLine($"[UDP:{Port}] First packet from {result.RemoteEndPoint}");
                }

                ParseMavlink(result.Buffer, result.Buffer.Length);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException ex)
        {
            Console.WriteLine($"[UDP:{Port}] Socket error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UDP:{Port}] Receive loop died: {ex.Message}");
        }
    }

    private void ParseMavlink(byte[] data, int len)
    {
        int i = 0;
        while (i < len)
        {
            if (data[i] != MavlinkStx)
            {
                i++;
                continue;
            }

            if (i + 1 + HeaderLen > len)
                break;

            int headerStart = i + 1;
            byte payloadLen = data[headerStart];
            int msgId = data[headerStart + 6]
                      | (data[headerStart + 7] << 8)
                      | (data[headerStart + 8] << 16);

            int frameLen = 1 + HeaderLen + payloadLen + CrcLen;
            if (i + frameLen > len)
                break;

            if (msgId == RawImuMsgId && payloadLen >= 10)
            {
                int payloadStart = headerStart + HeaderLen;
                short xacc = (short)(data[payloadStart + 8] | (data[payloadStart + 9] << 8));
                _subject.OnNext(xacc);

                var count = Interlocked.Increment(ref _totalPackets);
                if (count % 500 == 0)
                    Console.WriteLine($"[UDP:{Port}] {count} packets, last xacc={xacc}");
            }

            i += frameLen;
        }
    }

    private async Task HeartbeatWatchdog(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, ct);
                var lastTicks = Interlocked.Read(ref _lastReceivedTicks);
                if (_connected.Value && lastTicks > 0 &&
                    (DateTime.UtcNow.Ticks - lastTicks) > TimeSpan.FromSeconds(3).Ticks)
                {
                    _connected.OnNext(false);
                    Console.WriteLine($"[UDP:{Port}] Heartbeat timeout — no data for 3s");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _subject.Dispose();
        _connected.Dispose();
    }
}
