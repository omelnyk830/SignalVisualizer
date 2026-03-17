namespace SignalVisualizer.Services;

public sealed class MavlinkSourceFactory : ISignalSourceFactory
{
    public ISignalSource Create(string portName, int baudRate)
        => new MavlinkSignalSource(portName, baudRate);
}