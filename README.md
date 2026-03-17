# SignalVisualizer

Real-time signal visualization app built with Avalonia and ScottPlot. Supports multiple signal sources: mock sine wave, STM32 serial (MAVLink / custom binary), iPhone GPS (HTTP), and WiFi RSSI.

## Prerequisites

- .NET 9.0 SDK
- macOS / Linux / Windows (WiFi source is macOS-only)

## Quick Start

```bash
cd SignalVisualizer
dotnet run
```

## Signal Sources

The app uses a pluggable `ISignalSource` interface. Switch the active source in `App.axaml.cs`:

### Mock Sine Wave (no hardware needed)

```csharp
var source = new MockSignalSource(frequencyHz: 1.0, samplesPerSecond: 100);
```

Generates a synthetic sine signal. Good for testing the UI and pipeline.

### STM32 Serial — MAVLink (recommended for hardware)

```csharp
var source = new MavlinkSignalSource("/dev/tty.usbmodemXXXXX");
```

Reads MAVLink v2 RAW_IMU messages from an STM32 board over USB serial.

**Setup:**
1. Flash the firmware (see [Firmware](#firmware) section)
2. Plug in the Nucleo board via USB
3. Find the port: `ls /dev/tty.usb*`
4. Update the port name in `App.axaml.cs`

### STM32 Serial — Custom Binary

```csharp
var source = new SerialSignalSource("/dev/tty.usbmodemXXXXX");
```

Reads 4-byte binary frames: `[0xAA] [high] [low] [XOR checksum]`. Use this with the older firmware that doesn't use MAVLink.

### iPhone GPS (Sensor Logger app)

```csharp
var source = new HttpSignalSource(port: 5000, field: "altitude");
```

Receives GPS data from the [Sensor Logger](https://apps.apple.com/app/sensor-logger/id1531582925) iOS app via HTTP POST.

**Setup:**
1. Install Sensor Logger on your iPhone
2. Find your Mac IP: `ipconfig getifaddr en0`
3. In Sensor Logger: Settings > HTTP Push > set URL to `http://<your-mac-ip>:5000`
4. Enable Location sensor
5. Hit record

Available fields: `altitude`, `speed`, `latitude`, `longitude`

### WiFi Signal Strength (macOS only)

```csharp
var source = new WifiSignalSource(samplesPerSecond: 2);
```

Reads WiFi RSSI via CoreWLAN. Values range from -30 (strong) to -90 dBm (weak). Walk around to see the signal change.

## Architecture

```
SignalSource (background thread)
    |  IObservable<double> — raw samples
    v
SignalProcessor (background thread)
    |  IObservable<IList<double>> — batched every 50ms
    v
MainWindowViewModel (UI thread via Dispatcher.Post)
    |  Updates DataBuffer[] + PacketLog
    v
MainWindow
    |  ScottPlot chart + packet log ListBox
    v
Screen
```

### Key design decisions

- **Reactive streams** (`System.Reactive`) for the entire data pipeline
- **Background collection, UI batching** — signals are collected at full rate on a thread pool thread, batched into 50ms chunks, then marshalled to the UI thread
- **Rolling buffer** — `DataBuffer` is a fixed `double[1000]` array that ScottPlot reads directly (no copies)
- **Pluggable sources** — all sources implement `ISignalSource`, swap in `App.axaml.cs`

## UI Controls

- **Pause / Resume** — freezes both the chart and the packet log
- **Clear** — resets the packet log and counter
- **Packet log** — shows raw frame bytes, decoded ADC value, voltage, and temperature

## Project Structure

```
SignalVisualizer/
├── Services/
│   ├── ISignalSource.cs          # Source interface
│   ├── ISignalProcessor.cs       # Processor interface
│   ├── SignalProcessor.cs         # Buffers raw stream into 50ms batches
│   ├── MockSignalSource.cs        # Synthetic sine wave
│   ├── MavlinkSignalSource.cs     # MAVLink v2 RAW_IMU parser
│   ├── SerialSignalSource.cs      # Custom binary frame parser
│   ├── HttpSignalSource.cs        # HTTP listener for Sensor Logger
│   └── WifiSignalSource.cs        # macOS WiFi RSSI
├── ViewModels/
│   ├── ViewModelBase.cs
│   └── MainWindowViewModel.cs
├── Views/
│   ├── MainWindow.axaml           # Layout: chart + toolbar + packet log
│   └── MainWindow.axaml.cs        # Wires ScottPlot to ViewModel
├── App.axaml.cs                   # Composition root (wire source here)
└── Program.cs                     # Entry point
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
- Sends the frame over USART2 (ST-Link virtual COM port) at 115200 baud, 100 times/sec
- Also supports text commands over UART: `MODE_SOS`, `MODE_STB`, `STATUS`

### MAVLink frame format

The firmware uses the standard MAVLink v2 protocol. You don't need to understand the wire format — the MAVLink library handles framing on both sides:

```
Firmware                        MAVLink C library              UART
"ADC = 1734"  ──────────>  [FD][len][seq][...][payload][CRC]  ──────>  wire

wire  ──────>  [FD][len][seq][...][payload][CRC]  ──────────>  "ADC = 1734"
                    MavlinkSignalSource parser                  ViewModel
```

## Debugging Serial Data

To inspect raw bytes from the board:

```bash
# Quick hex dump (Python)
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