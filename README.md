# DevTools WPF

A Windows desktop toolkit for Modbus diagnostics and on-site testing.

DevTools WPF includes:
- Modbus TCP Client
- Modbus TCP Server
- Modbus RTU Client
- Modbus RTU Server
- RTU Serial Scanner (multi-baud/frame discovery)
- Console command system for diagnostics and register dumps

## Highlights

- Live polling and editable register maps
- Shared server runtime state across view switches
- Packet-level diagnostics controls
- Preset-based workflow for quick reconnects
- Built-in help and troubleshooting guide

## Tech Stack

- .NET 8
- WPF (Windows)

## Quick Start

### Prerequisites
- Windows 10/11
- .NET SDK 8.0+

### Build

```powershell
dotnet build DevTools.sln
```

### Run

```powershell
dotnet run --project src/DevTools.Wpf/DevTools.Wpf.csproj
```

### Publish (Release)

```powershell
dotnet publish src/DevTools.Wpf/DevTools.Wpf.csproj -c Release -r win-x64 --self-contained false
```

## Console Commands

- /help
- /clear
- /status
- /loglevel info|debug
- /packets on|off
- /regs [tcp|rtu] all [start count]
- /sync [tcp|rtu|all]

## Release Notes

See [CHANGELOG.md](CHANGELOG.md) for version history and [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) for release validation.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
