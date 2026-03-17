namespace SignalVisualizer.Models;

public enum DroneEventType
{
    Connected,
    Disconnected,
}

public readonly record struct DroneEvent(DroneEventType Type, DroneSession Session);