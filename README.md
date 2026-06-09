# R&S SMW200A Controller

A C# WPF application for controlling the Rohde & Schwarz SMW200A signal generator. It scans the instrument for arbitrary waveform files already stored in its memory, loads a selected file onto one or both RF channels, then ramps the RF output power from −60 dBm to 0 dBm in 10 dBm steps with a 10 second dwell at each level.

---

## Features

- Enter IP address and port to connect to the instrument over TCP
- Scan the instrument's file system for `.wv` waveform files across all directories
- Filter the waveform list by name or path
- Load any scanned waveform onto **Channel 1**, **Channel 2**, or both — independently, allowing different waveforms per channel
- Start RF output on both channels simultaneously — automatically ramps power from −60 → 0 dBm (10 dBm steps, 10 s dwell each)
- Stop RF output on both channels at any time during the ramp
- Live status bar and current level readout

---

## Requirements

- Windows (WPF is Windows-only)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- R&S SMW200A reachable on the network (default SCPI port: **5025**)

---

## Build & Run

```bash
cd SmwController
dotnet restore
dotnet build
dotnet run
```

Or open `SmwController.csproj` in Visual Studio 2022+ and press F5.

---

## Usage

1. Enter the instrument's IP address and port, then click **Connect**.
2. Set the root path to scan (default: `/var/usr`) and click **Scan Instrument**.
3. Use the search box to filter the waveform list, then select a file.
4. Click **Load to Channel 1** or **Load to Channel 2** to load the selected waveform onto the desired channel. Repeat with a different selection for the other channel if needed.
5. Click **▶ Start RF** to begin the power ramp on both channels.
6. Click **■ Stop RF** at any time to disable RF output on both channels.

---

## Project Structure

```
SmwController/
├── App.xaml / App.xaml.cs          # Application entry; dependency injection setup
├── MainWindow.xaml / .cs           # Main UI window
├── Services/
│   ├── ISmwService.cs              # Interface for all instrument operations
│   └── SmwService.cs               # Raw TCP/SCPI implementation
└── ViewModels/
    ├── MainViewModel.cs            # All UI logic and commands
    ├── RelayCommand.cs             # RelayCommand + AsyncRelayCommand
    ├── PathConverters.cs           # IValueConverters for directory/filename display
    └── InverseBoolConverter.cs     # Bool inversion for XAML bindings
```

### NuGet Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.DependencyInjection` | Constructor injection of `ISmwService` into `MainViewModel` |

---

## SCPI Commands Used

All commands are sent as ASCII text over a raw TCP socket (terminated with `\n`). `{ch}` is `1` or `2` depending on the target channel.

| Operation | Command |
|-----------|---------|
| Identify instrument | `*IDN?` |
| List directory contents | `:MMEMory:CATalog? "<path>"` |
| Select waveform in ARB | `:SOURce{ch}:BB:ARBitrary:WAVeform:SELect "<path>"` |
| Enable ARB generator | `:SOURce{ch}:BB:ARBitrary:STATe ON` |
| Set output power | `:SOURce{ch}:POWer:LEVel:IMMediate:AMPLitude <dBm>` |
| Enable RF output | `:OUTPut{ch}:STATe ON` |
| Disable RF output | `:OUTPut{ch}:STATe OFF` |

Waveform files are selected from instrument memory — no file upload from the PC is performed.

---

## RF Ramp Sequence

Both channels are controlled simultaneously with identical power levels throughout the ramp.

1. Set power to −60 dBm on both channels and enable RF output
2. Dwell 10 seconds at each level
3. Step up by 10 dBm and repeat until 0 dBm is reached
4. Hold at 0 dBm on both channels until the user presses **Stop RF**
5. RF output is disabled on both channels on stop

| Parameter | Value |
|-----------|-------|
| Start level | −60 dBm |
| Stop level | 0 dBm |
| Step size | 10 dBm |
| Dwell time | 10 seconds |

---

## Architecture

The app follows **MVVM** with Microsoft dependency injection:

```
App.xaml.cs
  └── ServiceCollection
        ├── SmwService        (singleton, ISmwService)
        ├── MainViewModel     (singleton, injected with ISmwService)
        └── MainWindow        (singleton, injected with MainViewModel)
```

`SmwService` serialises all SCPI traffic through a `SemaphoreSlim` lock to prevent concurrent socket writes.
