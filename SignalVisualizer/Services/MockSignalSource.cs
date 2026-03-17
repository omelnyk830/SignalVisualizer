using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace SignalVisualizer.Services;

public class MockSignalSource : ISignalSource
{
    private readonly BehaviorSubject<bool> _running = new(false);

    public IObservable<double> SignalStream { get; }

    public MockSignalSource(double frequencyHz = 1.0, int samplesPerSecond = 100)
    {
        SignalStream = _running
            .DistinctUntilChanged()
            .Select(running => running
                ? Observable.Interval(TimeSpan.FromMilliseconds(1000.0 / samplesPerSecond))
                    .Select(tick =>
                    {
                        var t = tick / (double)samplesPerSecond;
                        return Math.Sin(2 * Math.PI * frequencyHz * t);
                    })
                : Observable.Empty<double>())
            .Switch()
            .Publish()
            .RefCount();
    }

    public void Start() => _running.OnNext(true);
    public void Stop() => _running.OnNext(false);
}