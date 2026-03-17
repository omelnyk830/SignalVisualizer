using System;

namespace SignalVisualizer.Services;

public interface ICommandSource
{
    void SendCommand(ReadOnlySpan<char> command);
}