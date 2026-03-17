using System.Net;
using DroneMock;

int droneCount = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 3;

Console.WriteLine($"=== DroneMock: launching {droneCount} simulated UAVs ===\n");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var drones = new List<MockDrone>();
var tasks = new List<Task>();

for (int i = 0; i < droneCount; i++)
{
    int dataPort = 14560 + i;
    var droneId = $"MOCK-{i + 1:D2}";

    var drone = new MockDrone(
        droneId: droneId,
        dataPort: dataPort,
        groundStation: new IPEndPoint(IPAddress.Loopback, dataPort),
        discoveryPort: 14550,
        samplesPerSecond: 100,
        signalFrequencyHz: 0.5 + (i * 0.3),
        signalAmplitude: 1500 + (i * 500),
        noiseLevel: 30 + (i * 20));

    drones.Add(drone);
    tasks.Add(drone.RunAsync(cts.Token));

    Console.WriteLine($"  {droneId} -> UDP :{dataPort}  (freq={0.5 + i * 0.3:F1}Hz, amp={1500 + i * 500})");
}

Console.WriteLine($"\nAll {droneCount} drones transmitting. Discovery on UDP :14550\n");
Console.WriteLine("--- KEYBOARD CONTROLS ---");
Console.WriteLine("  1-9    Select drone (1=MOCK-01, 2=MOCK-02, ...)");
Console.WriteLine("  S      Spike — send max/min values (visible spike on chart)");
Console.WriteLine("  D      Dropout — send zeros (flatline)");
Console.WriteLine("  G      Garbage — send random bytes (parser should ignore)");
Console.WriteLine("  A      All drones spike at once");
Console.WriteLine("  Ctrl+C Quit\n");

int selectedDrone = 0;
Console.WriteLine($"  >> Selected: MOCK-{selectedDrone + 1:D2}\n");

// Keyboard input loop
var keyboardTask = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        if (!Console.KeyAvailable)
        {
            await Task.Delay(50, cts.Token);
            continue;
        }

        var key = Console.ReadKey(intercept: true);

        if (key.Key >= ConsoleKey.D1 && key.Key <= ConsoleKey.D9)
        {
            int idx = key.Key - ConsoleKey.D1;
            if (idx < drones.Count)
            {
                selectedDrone = idx;
                Console.WriteLine($"  >> Selected: MOCK-{selectedDrone + 1:D2}");
            }
        }
        else switch (char.ToUpper(key.KeyChar))
        {
            case 'S':
                await drones[selectedDrone].InjectSpike();
                break;
            case 'D':
                await drones[selectedDrone].InjectDropout();
                break;
            case 'G':
                await drones[selectedDrone].InjectGarbage();
                break;
            case 'A':
                Console.WriteLine("  >> ALL DRONES: SPIKE!");
                foreach (var d in drones)
                    await d.InjectSpike();
                break;
        }
    }
});

try
{
    await Task.WhenAny(Task.WhenAll(tasks), keyboardTask);
}
catch (OperationCanceledException) { }

foreach (var drone in drones)
    drone.Dispose();

Console.WriteLine("\nAll drones stopped.");
