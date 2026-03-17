using System;
using System.Buffers;
using System.IO;
using System.IO.Ports;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace SignalVisualizer.Services;

/// <summary>
/// Reads MAVLink v2 RAW_IMU messages from a serial port.
/// Auto-detects STM32 ST-Link virtual COM port and reconnects on unplug/replug.
/// Zero-alloc hot path: uses ArrayPool + stackalloc, no per-frame heap allocations.
/// </summary>
public class MavlinkSignalSource : ISignalSource, ICommandSource, IDisposable
{
    private const byte MavlinkStx = 0xFD;
    private const int RawImuMsgId = 27;
    private const int HeaderLen = 9;
    private const int CrcLen = 2;
    private const int MaxPayload = 255;

    private readonly string _portPattern;
    private readonly int _baudRate;
    private readonly Subject<double> _subject = new();
    private readonly BehaviorSubject<bool> _connected = new(false);
    private SerialPort? _port;
    private CancellationTokenSource? _cts;

    public IObservable<double> SignalStream => _subject.AsObservable();
    public IObservable<bool> ConnectionState => _connected.AsObservable();
    public bool IsConnected => _connected.Value;

    /// <param name="portPattern">Glob pattern to match port, e.g. "usbmodem" matches /dev/tty.usbmodem*</param>
    /// <param name="baudRate">Serial baud rate</param>
    public MavlinkSignalSource(string portPattern = "usbmodem", int baudRate = 115200)
    {
        _portPattern = portPattern;
        _baudRate = baudRate;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => ConnectionLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        ClosePort();
    }

    private void ConnectionLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_port == null || !_port.IsOpen)
                {
                    _connected.OnNext(false);
                    var portName = FindPort();

                    if (portName == null)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    _port = new SerialPort(portName, _baudRate, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 1000,
                    };
                    _port.Open();
                    _connected.OnNext(true);
                    Console.WriteLine($"[MavLink] Connected: {portName}");
                }

                ReadLoop(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"[MavLink] Disconnected: {ex.Message}");
                ClosePort();
                _connected.OnNext(false);
                Thread.Sleep(1000);
            }
        }
    }

    private void ReadLoop(CancellationToken ct)
    {
        // Single rented buffer for the entire read session — zero per-frame allocations
        byte[] buf = ArrayPool<byte>.Shared.Rent(HeaderLen + MaxPayload + CrcLen);
        try
        {
            ReadLoopCore(ct, buf);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private void ReadLoopCore(CancellationToken ct, byte[] buf)
    {
        // Layout inside buf:
        //   [0..8]   = header  (9 bytes)
        //   [9..9+payloadLen-1] = payload
        //   [9+payloadLen..9+payloadLen+1] = crc
        while (!ct.IsCancellationRequested && _port is { IsOpen: true })
        {
            try
            {
                int b = _port.ReadByte();
                if (b != MavlinkStx)
                    continue;

                ReadExact(buf, 0, HeaderLen);

                byte payloadLen = buf[0];
                int msgId = buf[6] | (buf[7] << 8) | (buf[8] << 16);

                ReadExact(buf, HeaderLen, payloadLen);
                ReadExact(buf, HeaderLen + payloadLen, CrcLen);

                if (msgId == RawImuMsgId && payloadLen >= 10)
                {
                    int payloadStart = HeaderLen;
                    short xacc = (short)(buf[payloadStart + 8] | (buf[payloadStart + 9] << 8));
                    _subject.OnNext(xacc);
                }
            }
            catch (TimeoutException)
            {
                // No data — keep waiting
            }
            catch (IOException)
            {
                break;
            }
            catch (UnauthorizedAccessException)
            {
                break;
            }
        }
    }

    private string? FindPort()
    {
        foreach (var name in SerialPort.GetPortNames())
        {
            if (name.Contains(_portPattern, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return null;
    }

    private void ReadExact(byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            int read = _port!.Read(buffer, offset, count);
            offset += read;
            count -= read;
        }
    }

    private void ClosePort()
    {
        try
        {
            if (_port is { IsOpen: true })
                _port.Close();
        }
        catch { /* already gone */ }
        _port?.Dispose();
        _port = null;
    }

    public void SendCommand(ReadOnlySpan<char> command)
    {
        if (_port is { IsOpen: true })
            _port.Write(string.Concat(command, "\r\n"));
    }

    public void Dispose()
    {
        Stop();
        _subject.Dispose();
        _connected.Dispose();
    }
}