using System;

namespace SignalVisualizer.Services;

/// <summary>
/// Optional interface for signal sources that support connection state tracking.
/// Sources that don't implement this are assumed always-connected.
/// </summary>
public interface IConnectionAware
{
    IObservable<bool> ConnectionState { get; }
}