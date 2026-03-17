using System;
using System.Collections.Generic;
using System.Reactive.Linq;

namespace SignalVisualizer.Services;

public class SignalProcessor: ISignalProcessor, IDisposable
{
    private readonly ISignalSource _source;
    public IObservable<IList<double>> ProcessedStream { get; }
    public SignalProcessor(ISignalSource source)
    {
        _source = source;
        
        ProcessedStream = _source.SignalStream.Buffer(TimeSpan.FromMilliseconds(50)).Publish().RefCount();
    }
    public void Start()
    {
        _source.Start();
    }

    public void Stop()
    {
        _source.Stop();
    }

    public void Dispose()
    {
        Stop();
        (_source as IDisposable)?.Dispose();
    }
}