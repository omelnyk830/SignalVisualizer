namespace SignalVisualizer.Services;

public interface ICommandSource
{
    void SendCommand(string command);
}