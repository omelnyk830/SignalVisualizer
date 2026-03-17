using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DroneMock;

/// <summary>
/// Simulates a single drone sending MAVLink v2 RAW_IMU frames over UDP.
/// Sends discovery announcements to the ground station, then streams telemetry.
/// </summary>
public sealed class MockDrone : IDisposable
{
    private const byte MavlinkStx = 0xFD;
    private const int RawImuMsgId = 27;
    private const int RawImuPayloadLen = 26; // RAW_IMU has 26 bytes payload

    private readonly string _droneId;
    private readonly int _dataPort;
    private readonly IPEndPoint _groundStation;
    private readonly int _discoveryPort;
    private readonly int _samplesPerSecond;
    private readonly double _signalFrequencyHz;
    private readonly double _signalAmplitude;
    private readonly double _noiseLevel;

    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private byte _sequenceNumber;

    public MockDrone(
        string droneId,
        int dataPort,
        IPEndPoint groundStation,
        int discoveryPort = 14550,
        int samplesPerSecond = 100,
        double signalFrequencyHz = 1.0,
        double signalAmplitude = 2000.0,
        double noiseLevel = 50.0)
    {
        _droneId = droneId;
        _dataPort = dataPort;
        _groundStation = groundStation;
        _discoveryPort = discoveryPort;
        _samplesPerSecond = samplesPerSecond;
        _signalFrequencyHz = signalFrequencyHz;
        _signalAmplitude = signalAmplitude;
        _noiseLevel = noiseLevel;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _client = new UdpClient();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var announcerTask = AnnounceLoop(_cts.Token);
        var telemetryTask = TelemetryLoop(_cts.Token);

        await Task.WhenAll(announcerTask, telemetryTask);
    }

    /// <summary>
    /// Sends "HELLO:{droneId}:{dataPort}" to discovery port every 2 seconds.
    /// </summary>
    private async Task AnnounceLoop(CancellationToken ct)
    {
        var discoveryEndpoint = new IPEndPoint(_groundStation.Address, _discoveryPort);
        var message = Encoding.UTF8.GetBytes($"HELLO:{_droneId}:{_dataPort}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _client!.SendAsync(message, discoveryEndpoint, ct);
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Sends MAVLink v2 RAW_IMU frames at the configured rate.
    /// xacc = sine wave + noise (simulates accelerometer on vibrating airframe).
    /// </summary>
    private async Task TelemetryLoop(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / _samplesPerSecond);
        var rng = new Random();
        long tick = 0;

        // Reusable frame buffer: STX(1) + header(9) + payload(26) + CRC(2) = 38 bytes
        byte[] frame = new byte[1 + 9 + RawImuPayloadLen + 2];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var t = tick / (double)_samplesPerSecond;
                var signal = _signalAmplitude * Math.Sin(2 * Math.PI * _signalFrequencyHz * t);
                var noise = (rng.NextDouble() - 0.5) * 2.0 * _noiseLevel;
                short xacc = (short)Math.Clamp(signal + noise, short.MinValue, short.MaxValue);

                int len = BuildRawImuFrame(frame, xacc, tick * 10_000); // time_usec
                await _client!.SendAsync(frame.AsMemory(0, len), _groundStation, ct);

                // Print every 100th sample so you can see data flowing
                if (tick % 100 == 0)
                    Console.WriteLine($"  [{_droneId}] #{tick,6} xacc={xacc,6} (signal={signal:F0})");

                tick++;
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Builds a minimal MAVLink v2 RAW_IMU frame in the provided buffer.
    /// Returns the total frame length.
    /// </summary>
    private int BuildRawImuFrame(byte[] buf, short xacc, long timeUsec)
    {
        int i = 0;

        // STX
        buf[i++] = MavlinkStx;

        // Header (9 bytes)
        buf[i++] = RawImuPayloadLen;            // payload length
        buf[i++] = 0;                           // incompat flags
        buf[i++] = 0;                           // compat flags
        buf[i++] = _sequenceNumber++;           // sequence
        buf[i++] = 1;                           // system ID
        buf[i++] = 1;                           // component ID
        buf[i++] = RawImuMsgId & 0xFF;          // msg ID low
        buf[i++] = (byte)(RawImuMsgId >> 8);    // msg ID mid
        buf[i++] = (byte)(RawImuMsgId >> 16);   // msg ID high

        // Payload (26 bytes) — RAW_IMU field order per MAVLink spec:
        //   time_usec (8 bytes, uint64)
        //   xacc (2 bytes, int16) @ offset 8
        //   yacc, zacc, xgyro, ygyro, zgyro, xmag, ymag, zmag (2 bytes each)
        int payloadStart = i;

        // time_usec (little-endian uint64)
        buf[payloadStart + 0] = (byte)(timeUsec);
        buf[payloadStart + 1] = (byte)(timeUsec >> 8);
        buf[payloadStart + 2] = (byte)(timeUsec >> 16);
        buf[payloadStart + 3] = (byte)(timeUsec >> 24);
        buf[payloadStart + 4] = (byte)(timeUsec >> 32);
        buf[payloadStart + 5] = (byte)(timeUsec >> 40);
        buf[payloadStart + 6] = (byte)(timeUsec >> 48);
        buf[payloadStart + 7] = (byte)(timeUsec >> 56);

        // xacc (little-endian int16) @ offset 8
        buf[payloadStart + 8] = (byte)(xacc);
        buf[payloadStart + 9] = (byte)(xacc >> 8);

        // Zero out remaining fields (yacc through zmag)
        Array.Clear(buf, payloadStart + 10, RawImuPayloadLen - 10);

        i += RawImuPayloadLen;

        // CRC (simplified — two zero bytes; real CRC not validated by our parser)
        buf[i++] = 0;
        buf[i++] = 0;

        return i;
    }

    /// <summary>
    /// Sends a burst of bad data — valid MAVLink frames with extreme xacc values.
    /// Call from the keyboard handler to see spikes on the chart.
    /// </summary>
    public async Task InjectSpike(int count = 20)
    {
        if (_client == null) return;
        byte[] frame = new byte[1 + 9 + RawImuPayloadLen + 2];

        for (int j = 0; j < count; j++)
        {
            // Alternating max/min — creates a visible spike pattern
            short bad = (j % 2 == 0) ? short.MaxValue : short.MinValue;
            int len = BuildRawImuFrame(frame, bad, 0);
            await _client.SendAsync(frame.AsMemory(0, len), _groundStation);
        }
        Console.WriteLine($"  [{_droneId}] ** INJECTED {count} spike packets (xacc={short.MaxValue}/{short.MinValue}) **");
    }

    /// <summary>
    /// Sends frames with xacc=0 (flatline / signal loss).
    /// </summary>
    public async Task InjectDropout(int count = 50)
    {
        if (_client == null) return;
        byte[] frame = new byte[1 + 9 + RawImuPayloadLen + 2];

        for (int j = 0; j < count; j++)
        {
            int len = BuildRawImuFrame(frame, 0, 0);
            await _client.SendAsync(frame.AsMemory(0, len), _groundStation);
        }
        Console.WriteLine($"  [{_droneId}] ** INJECTED {count} dropout packets (xacc=0) **");
    }

    /// <summary>
    /// Sends raw garbage bytes — not valid MAVLink. Tests parser resilience.
    /// </summary>
    public async Task InjectGarbage(int bytes = 100)
    {
        if (_client == null) return;
        var garbage = new byte[bytes];
        Random.Shared.NextBytes(garbage);
        await _client.SendAsync(garbage, _groundStation);
        Console.WriteLine($"  [{_droneId}] ** INJECTED {bytes} bytes of garbage **");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _client?.Dispose();
    }
}
