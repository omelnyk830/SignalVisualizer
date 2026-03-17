using System;
using System.IO.Ports;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace SignalVisualizer.Services;

/// <summary>
/// Reads MAVLink v2 RAW_IMU messages from a serial port.
/// Minimal parser — no external MAVLink library needed.
///
/// MAVLink v2 frame:
/// [FD] [len] [incompat] [compat] [seq] [sysid] [compid] [msgid0] [msgid1] [msgid2] [payload...] [crc_lo] [crc_hi]
///
/// RAW_IMU (msg id 27) payload (29 bytes):
///   time_usec  (8 bytes, uint64)
///   xacc       (2 bytes, int16)  ← we read this (our ADC value)
///   yacc       (2 bytes, int16)
///   zacc       (2 bytes, int16)
///   xgyro      (2 bytes, int16)
///   ygyro      (2 bytes, int16)
///   zgyro      (2 bytes, int16)
///   xmag       (2 bytes, int16)
///   ymag       (2 bytes, int16)
///   zmag       (2 bytes, int16)
///   id         (1 byte,  uint8)
///   temperature(2 bytes, int16)
/// </summary>
public class MavlinkSignalSource : ISignalSource, ICommandSource, IDisposable
{
    private const byte MavlinkStx = 0xFD;       // MAVLink v2 start byte
    private const int RawImuMsgId = 27;
    private const int RawImuPayloadLen = 29;     // MAVLink v2 RAW_IMU payload size

    private readonly SerialPort _port;
    private readonly Subject<double> _subject = new();
    private CancellationTokenSource? _cts;

    public IObservable<double> SignalStream => _subject.AsObservable();

    public MavlinkSignalSource(string portName, int baudRate = 115200)
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
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1. Sync — find MAVLink v2 header
                int b = _port.ReadByte();
                if (b != MavlinkStx)
                    continue;

                // 2. Read header (9 bytes after STX)
                var header = new byte[9];
                ReadExact(header, 0, 9);

                byte payloadLen = header[0];    // len
                // header[1] = incompat_flags
                // header[2] = compat_flags
                // header[3] = seq
                // header[4] = sysid
                // header[5] = compid
                int msgId = header[6] | (header[7] << 8) | (header[8] << 16);

                // 3. Read payload + 2 bytes CRC
                var payload = new byte[payloadLen];
                ReadExact(payload, 0, payloadLen);

                var crc = new byte[2];
                ReadExact(crc, 0, 2);

                // 4. If RAW_IMU, extract xacc (bytes 8-9 of payload, little-endian int16)
                if (msgId == RawImuMsgId && payloadLen >= 10)
                {
                    short xacc = (short)(payload[8] | (payload[9] << 8));
                    _subject.OnNext(xacc);
                }
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

    private void ReadExact(byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            int read = _port.Read(buffer, offset, count);
            offset += read;
            count -= read;
        }
    }

    public void SendCommand(string command)
    {
        if (_port.IsOpen)
            _port.Write(command + "\r\n");
    }

    public void Dispose()
    {
        Stop();
        _subject.Dispose();
        _port.Dispose();
    }
}