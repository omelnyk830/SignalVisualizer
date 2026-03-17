using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;

namespace SignalVisualizer.Services;

public class WifiSignalSource : ISignalSource
{
    private readonly BehaviorSubject<bool> _running = new(false);

    public IObservable<double> SignalStream { get; }

    public WifiSignalSource(int samplesPerSecond = 2)
    {
        SignalStream = _running
            .DistinctUntilChanged()
            .Select(running => running
                ? Observable.Interval(TimeSpan.FromMilliseconds(1000.0 / samplesPerSecond))
                    .Select(_ => ReadRssi())
                : Observable.Empty<double>())
            .Switch()
            .Publish()
            .RefCount();
    }

    private static double ReadRssi()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "swift",
            Arguments = "-e \"import CoreWLAN; let c = CWWiFiClient.shared(); if let i = c.interface() { print(i.rssiValue()) }\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi);
        var output = proc!.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit();

        return double.TryParse(output, out var rssi) ? rssi : 0;
    }

    public void Start() => _running.OnNext(true);
    public void Stop() => _running.OnNext(false);
}