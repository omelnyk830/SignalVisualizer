using System;
using System.Collections.Generic;

namespace SignalVisualizer.Services;

public interface ISignalProcessor                                                                                                                                             
{
    IObservable<IList<double>> ProcessedStream { get; }  // batched + windowed                                                                                                
    void Start();                                
    void Stop();                        
} 