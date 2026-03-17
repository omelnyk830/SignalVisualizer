using System;
using System.IO.Ports;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace SignalVisualizer.Services;

/// <summary>
/// Reads binary frames from STM32 over UART.
/// Frame format: [0xAA] [high byte] [low byte] [XOR checksum]
/// Payload is a 16-bit unsigned ADC value (0–4095 for 12-bit ADC).
/// </summary>
public class SerialSignalSource : ISignalSource, IDisposable
{
    private readonly SerialPort _port;
    private readonly Subject<double> _subject = new();
    private CancellationTokenSource? _cts;

    public IObservable<double> SignalStream => _subject.AsObservable();

    public SerialSignalSource(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 1000,
        };
    }

    public void Start()
    {
        _port.Open();
        _cts = new CancellationTokenSource();
        Task.Run(() => ReadLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        if (_port.IsOpen)
            _port.Close();
    }

    private void ReadLoop(CancellationToken ct)
    {
        var buffer = new byte[4];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Sync: find header byte 0xAA
                int b = _port.ReadByte();
                if (b != 0xAA)
                    continue;

                buffer[0] = 0xAA;

                // Read remaining 3 bytes (high, low, checksum)
                int offset = 1;
                while (offset < 4)
                {
                    int read = _port.Read(buffer, offset, 4 - offset);
                    offset += read;
                }

                // Verify checksum (XOR of first 3 bytes)
                byte checksum = (byte)(buffer[0] ^ buffer[1] ^ buffer[2]);
                if (checksum != buffer[3])
                {
                    Console.WriteLine($"[Serial] Bad checksum: expected {checksum:X2}, got {buffer[3]:X2}");
                    continue;
                }

                // Parse 16-bit ADC value
                ushort raw = (ushort)((buffer[1] << 8) | buffer[2]);
                _subject.OnNext(raw);
            }
            catch (TimeoutException)
            {
                // No data, keep waiting
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _subject.Dispose();
        _port.Dispose();
    }
}