using System;
using System.IO;
using System.Net;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SignalVisualizer.Services;

public class HttpSignalSource : ISignalSource, IDisposable
{
    private readonly HttpListener _listener;
    private readonly Subject<double> _subject = new();
    private CancellationTokenSource? _cts;
    private readonly string _field;

    public IObservable<double> SignalStream => _subject.AsObservable();

    /// <param name="port">Port to listen on</param>
    /// <param name="field">Which GPS field to emit: altitude, speed, latitude, longitude</param>
    public HttpSignalSource(int port = 5000, string field = "altitude")
    {
        _field = field;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:{port}/");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.HttpMethod == "POST")
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    var body = await reader.ReadToEndAsync(ct);
                    Console.WriteLine($"[HttpSignalSource] Received {body.Length} bytes");
                    ParseAndEmit(body);
                }

                context.Response.StatusCode = 200;
                context.Response.Close();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void ParseAndEmit(string json)
    {
        try
        {
            // Sensor Logger format:
            // { "payload": [{ "name": "location", "values": { "altitude": 150.5, "speed": 1.2, ... } }] }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("payload", out var payload))
            {
                foreach (var entry in payload.EnumerateArray())
                {
                    if (entry.TryGetProperty("name", out var name) &&
                        name.GetString() == "location" &&
                        entry.TryGetProperty("values", out var values) &&
                        values.TryGetProperty(_field, out var fieldValue))
                    {
                        var value = fieldValue.GetDouble();
                        Console.WriteLine($"[HttpSignalSource] {_field} = {value}");
                        _subject.OnNext(value);
                    }
                }
            }
        }
        catch
        {
            // skip malformed JSON
        }
    }

    public void Dispose()
    {
        Stop();
        _subject.Dispose();
    }
}