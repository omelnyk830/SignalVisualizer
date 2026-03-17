# SignalVisualizer

Real-time multi-drone telemetry visualization built with Avalonia and ScottPlot. Supports USB serial (MAVLink / custom binary), UDP radio, iPhone GPS (HTTP), WiFi RSSI, and simulated mock drones.

## Prerequisites

- .NET 9.0 SDK
- macOS / Linux / Windows (WiFi source is macOS-only)

## Quick Start

```bash
# Terminal 1 — launch 3 simulated drones
dotnet run --project DroneMock

# Terminal 2 — launch the ground station UI
dotnet run --project SignalVisualizer
```

Each mock drone gets its own tab in the UI. Plug in a real STM32 board and it shows up as another tab automatically.

## Multi-Drone Architecture

```
                    ┌─ USB Serial ──> MavlinkSignalSource ─┐
DroneManager ───────┤                                      ├──> DroneSession ──> Tab
  (scans ports      ├─ USB Serial ──> SerialSignalSource  ─┤    per UAV
   + UDP discovery) │                                      │
                    └─ UDP Radio  ──> UdpSignalSource     ─┘

UdpDroneDiscovery ──── listens :14550 ──> "HELLO:{id}:{port}" ──> DroneManager
```

- **DroneManager** — singleton service that scans serial ports (1s interval) and listens for UDP announcements
- **ISignalSourceFactory** — creates the right source type per transport (swap one DI line to change protocol)
- **DroneSession** — per-UAV: owns its signal source, telemetry streams, and command channel
- **DroneTabViewModel** — per-tab: own plot buffer, stats, pause, commands

New UAVs are auto-detected. Unplug or stop transmitting → tab removed.

## DroneMock — Simulated UAVs

A standalone console app that simulates N drones sending MAVLink telemetry over UDP.

```bash
dotnet run --project DroneMock         # 3 drones (default)
dotnet run --project DroneMock -- 5    # 5 drones
```

Each mock drone has different signal characteristics (frequency, amplitude, noise) so you can tell them apart on the chart.

### Interactive Controls

While DroneMock is running, press keys to inject anomalies:

| Key | Action | What you'll see on the chart |
|-----|--------|------------------------------|
| `1`-`9` | Select drone | Sets which drone receives injections |
| `S` | Spike | 20 frames of `xacc = +32767/-32768` — massive spike |
| `D` | Dropout | 50 frames of `xacc = 0` — flatline to zero |
| `G` | Garbage | 100 random bytes — parser ignores, chart unchanged |
| `A` | All spike | Every drone spikes at once |
| `Ctrl+C` | Quit | Stops all drones |

### Discovery Protocol

Mock drones announce themselves every 2 seconds on UDP port 14550:

```
HELLO:{droneId}:{dataPort}
```

The ground station's `UdpDroneDiscovery` picks this up and creates a `UdpSignalSource` listening on `dataPort`. Telemetry flows as MAVLink v2 RAW_IMU frames on that port.

## Signal Sources

### MAVLink Serial (STM32 over USB)

```csharp
services.AddSingleton<ISignalSourceFactory, MavlinkSourceFactory>();
```

Auto-detects STM32 ports by pattern (`"usbmodem"`), auto-reconnects on unplug/replug. Reads MAVLink v2 RAW_IMU messages. Zero per-frame heap allocations (ArrayPool).

### Custom Binary Serial

```csharp
services.AddSingleton<ISignalSourceFactory, SerialSourceFactory>();
```

Reads 4-byte binary frames: `[0xAA] [high] [low] [XOR checksum]`. For firmware that doesn't use MAVLink.

### UDP Radio

Automatically enabled alongside serial scanning. Drones discovered via `UdpDroneDiscovery` on port 14550. Each drone streams telemetry to its own UDP data port.

### Mock Sine Wave (standalone, no DroneMock needed)

Register directly in DI for single-source testing:

```csharp
services.AddSingleton<ISignalSource>(_ => new MockSignalSource(frequencyHz: 1.0, samplesPerSecond: 100));
```

### iPhone GPS (Sensor Logger app)

```csharp
services.AddSingleton<ISignalSource>(_ => new HttpSignalSource(port: 5000, field: "altitude"));
```

Receives GPS data from [Sensor Logger](https://apps.apple.com/app/sensor-logger/id1531582925) via HTTP POST. Available fields: `altitude`, `speed`, `latitude`, `longitude`.

### WiFi Signal Strength (macOS only)

```csharp
services.AddSingleton<ISignalSource>(_ => new WifiSignalSource(samplesPerSecond: 2));
```

Reads WiFi RSSI via CoreWLAN. Values: -30 (strong) to -90 dBm (weak).

## Dependency Injection

All wiring happens in `App.axaml.cs`:

```csharp
services.AddSingleton<ISignalSourceFactory, MavlinkSourceFactory>();  // swap protocol here
services.AddSingleton<UdpDroneDiscovery>();
services.AddSingleton(sp => new DroneManager(
    sp.GetRequiredService<ISignalSourceFactory>(),
    sp.GetRequiredService<UdpDroneDiscovery>(),
    portPattern: "usbmodem"));
services.AddSingleton<MainWindowViewModel>();
```

## Data Pipeline

```
SignalSource (background thread)
    |  IObservable<double> — raw samples, 100/sec per drone
    v
SignalProcessor (per drone)
    |  IObservable<IList<double>> — batched every 50ms (~20 UI updates/sec)
    v
DroneTabViewModel (UI thread via Dispatcher.Post)
    |  Updates DataBuffer[1000] ring buffer
    v
DroneTabView
    |  ScottPlot chart, 30fps frame limiter
    v
Screen
```

- **Reactive streams** (`System.Reactive`) end-to-end
- **Background collection, UI batching** — full-rate capture, 50ms batch, marshal to UI
- **Ring buffer** — fixed `double[1000]` that ScottPlot reads directly (no copies)
- **Zero-alloc hot path** — `ArrayPool` for serial/UDP buffers, no per-frame `new byte[]`
- **Thread safety** — all UI-bound writes via `Dispatcher.UIThread.Post`, no locks

## Project Structure

```
SignalVisualizer/
├── Models/
│   ├── DroneSession.cs             # Per-UAV: source + telemetry streams
│   └── DroneEvent.cs               # Connected/Disconnected events
├── Services/
│   ├── ISignalSource.cs            # Source interface
│   ├── ISignalProcessor.cs         # Batched stream interface
│   ├── ISignalSourceFactory.cs     # Factory interface
│   ├── ICommandSource.cs           # Command channel (SOS, Standby, Status)
│   ├── IConnectionAware.cs         # Optional connection state tracking
│   ├── DroneManager.cs             # Port scanner + UDP discovery + session lifecycle
│   ├── UdpDroneDiscovery.cs        # Listens for drone announcements on UDP :14550
│   ├── UdpSignalSource.cs          # MAVLink over UDP receiver
│   ├── SignalProcessor.cs          # Buffers raw stream into 50ms batches
│   ├── MavlinkSignalSource.cs      # MAVLink v2 serial parser (auto-reconnect)
│   ├── MavlinkSourceFactory.cs     # Factory: creates MavlinkSignalSource
│   ├── SerialSignalSource.cs       # Custom binary frame parser
│   ├── SerialSourceFactory.cs      # Factory: creates SerialSignalSource
│   ├── MockSignalSource.cs         # Synthetic sine wave
│   ├── HttpSignalSource.cs         # HTTP listener for Sensor Logger
│   └── WifiSignalSource.cs         # macOS WiFi RSSI
├── ViewModels/
│   ├── ViewModelBase.cs            # Shared converters (Pause, ConnectionColor)
│   ├── MainWindowViewModel.cs      # Drone tab collection, DroneManager events
│   ├── DroneTabViewModel.cs        # Per-drone: plot buffer, stats, commands
│   └── PacketLogViewModel.cs       # Packet log window (scoped per window)
├── Views/
│   ├── MainWindow.axaml(.cs)       # TabControl bound to drone collection
│   ├── DroneTabView.axaml(.cs)     # Per-tab: ScottPlot chart + toolbar
│   └── PacketLogWindow.axaml(.cs)  # Separate packet log window
├── App.axaml.cs                    # DI composition root
└── Program.cs                      # Entry point
DroneMock/
├── MockDrone.cs                    # Simulates one UAV: discovery + MAVLink telemetry + inject
└── Program.cs                      # Launches N drones, keyboard controls
```

## Firmware

The companion STM32F103RB firmware lives in a separate repository:

```
Hardware-to-Cloud-Telemetry-Gateway/src/Firmware/
```

### Build and flash

```bash
cd src/Firmware
make          # build
make flash    # flash to Nucleo board via ST-Link
make serial   # open serial terminal (for text commands)
make size     # show Flash/RAM usage
```

**Requires:** `arm-none-eabi-gcc`, `openocd`

### What the firmware does

- Reads the STM32's internal temperature sensor (ADC channel 16)
- Packs the raw 12-bit ADC value into a MAVLink v2 RAW_IMU message
- Sends over USART2 (ST-Link virtual COM port) at 115200 baud, 100 samples/sec
- Supports text commands over UART: `MODE_SOS`, `MODE_STB`, `STATUS`

## Debugging

### Console output

Both the visualizer and DroneMock print diagnostic info:

```
[Discovery] Listening on UDP :14550
[DroneManager] MOCK-01 discovered via UDP on port 14560
[UDP:14560] First packet from 127.0.0.1:52341
[UDP:14560] 500 packets, last xacc=1234
```

### Inspect serial bytes

```bash
python3 -c "
import serial
ser = serial.Serial('/dev/cu.usbmodemXXXXX', 115200, timeout=2)
data = ser.read(100)
ser.close()
print('Hex:', data.hex(' '))
"
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Avalonia | 11.3.12 | Cross-platform UI framework |
| ScottPlot.Avalonia | 5.1.57 | Real-time signal plotting |
| System.Reactive | 6.1.0 | Reactive data pipeline |
| System.IO.Ports | 10.0.5 | Serial port communication |
| CommunityToolkit.Mvvm | 8.2.1 | MVVM infrastructure |
| Microsoft.Extensions.DependencyInjection | 10.0.5 | DI container |
