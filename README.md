# R&S SMW200A Controller

A C# WPF application for controlling the Rohde & Schwarz SMW200A signal generator. It loads an arbitrary waveform file onto the instrument and ramps the RF output power from −60 dBm to 0 dBm in 10 dBm steps with a 10 second dwell at each level.

---

## Features

- Enter IP address and port to connect to the instrument over TCP
- Browse for a `.wv` waveform file and upload it to the instrument
- Start RF output — automatically ramps power from −60 → 0 dBm (10 dBm steps, 10 s dwell each)
- Stop RF output at any time during the ramp
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
    └── InverseBoolConverter.cs     # Bool inversion for XAML bindings
```

### NuGet Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.DependencyInjection` | Constructor injection of `ISmwService` into `MainViewModel` |

---

## SCPI Commands Used

All commands are sent as ASCII text over a raw TCP socket (terminated with `\n`).

| Operation | Command |
|-----------|---------|
| Identify instrument | `*IDN?` |
| Upload waveform file | `:MMEMory:DATA "/var/user/<file>", #<N><len><bytes>` |
| Select waveform in ARB | `:SOURce1:BB:ARBitrary:WAVeform:SELect "/var/user/<name>"` |
| Enable ARB generator | `:SOURce1:BB:ARBitrary:STATe ON` |
| Set output power | `:SOURce1:POWer:LEVel:IMMediate:AMPLitude <dBm>` |
| Enable RF output | `:OUTPut1:STATe ON` |
| Disable RF output | `:OUTPut1:STATe OFF` |

File upload uses the **IEEE 488.2 arbitrary block data** format: `#<digits><bytecount><binary data>`.

---

## RF Ramp Sequence

1. Set power to −60 dBm and enable RF output
2. Dwell 10 seconds at each level
3. Step up by 10 dBm and repeat until 0 dBm is reached
4. Hold at 0 dBm until the user presses **Stop RF**
5. RF output is disabled on stop

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
