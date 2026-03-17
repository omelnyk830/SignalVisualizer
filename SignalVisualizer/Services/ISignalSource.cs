using System;

namespace SignalVisualizer.Services;

public interface ISignalSource
{
    // The stream of signal data (e.g., voltage or altitude)
    IObservable<double> SignalStream { get; }
    
    void Start();
    void Stop();
}