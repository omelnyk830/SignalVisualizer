namespace SignalVisualizer.Services;

/// <summary>
/// Creates signal source instances for a given serial port.
/// DroneManager uses this to produce the right source type per port.
/// Register different implementations to switch protocols (MAVLink, raw binary, etc.)
/// </summary>
public interface ISignalSourceFactory
{
    ISignalSource Create(string portName, int baudRate);
}