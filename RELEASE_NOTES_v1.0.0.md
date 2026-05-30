# DevTools WPF v1.0.0

Initial public release of DevTools WPF.

DevTools WPF is a Windows desktop toolkit for Modbus diagnostics and field testing, with client/server workflows for both TCP and RTU plus an RTU serial scanner for rapid device discovery.

## What's Included

- Modbus TCP Client
- Modbus TCP Server
- Modbus RTU Client
- Modbus RTU Server
- RTU Serial Scanner (multi-baud and frame combinations)
- Built-in console commands for diagnostics and register inspection
- Shared runtime/data-store behavior across views for stable state handling
- Expanded in-app help with quick start, command reference, and troubleshooting

## Key Commands

- `/help`
- `/clear`
- `/status`
- `/loglevel info|debug`
- `/packets on|off`
- `/regs [tcp|rtu] all [start count]`
- `/sync [tcp|rtu|all]`

## Highlights in v1

- WPF-first tool suite and navigation
- Improved scanner stop/cancel reliability and timeout behavior
- Better datastore-to-UI synchronization for server views
- Sparse mapped register dump support (`/regs ... all`)
- Theme support with persisted selection
- Branded app icon and polished shell UI

## Build and Run

### Requirements
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

### Publish

```powershell
dotnet publish src/DevTools.Wpf/DevTools.Wpf.csproj -c Release -r win-x64 --self-contained false
```

## Notes

- Presets are stored in the app output folder.
- If a COM port is in use by another tool, close the other process before connecting.

## License

DevTools Non-Resale License v1.0. See `LICENSE`.
