namespace SignalVisualizer.Services;

public sealed class SerialSourceFactory : ISignalSourceFactory
{
    public ISignalSource Create(string portName, int baudRate)
        => new SerialSignalSource(portName, baudRate);
}