# DevTools WPF v1 Release Checklist

## 1. Build and Packaging
- [ ] `dotnet build DevTools.sln` succeeds on a clean checkout.
- [ ] `dotnet publish src/DevTools.Wpf/DevTools.Wpf.csproj -c Release -r win-x64 --self-contained false` succeeds.
- [ ] Published app launches and icon appears correctly in title bar and taskbar.

## 2. Core Function Smoke Test
- [ ] Modbus TCP Server starts/stops cleanly.
- [ ] Modbus TCP Client connects and reads valid registers.
- [ ] Modbus RTU Server starts/stops cleanly.
- [ ] Modbus RTU Client opens port and reads valid registers.
- [ ] RTU Serial Scanner can start, stop, and cancel without hanging.

## 3. Diagnostics and Commands
- [ ] `/help` prints full command list.
- [ ] `/loglevel debug` enables packet diagnostics.
- [ ] `/packets on|off` toggles packet logging.
- [ ] `/regs tcp all` and `/regs rtu all` dump mapped values.
- [ ] `/sync all` refreshes server views from datastore.

## 4. UI and Persistence
- [ ] Theme switching works and persists across restart.
- [ ] Settings view opens with no ResourceDictionary warnings.
- [ ] Presets load/save for RTU/TCP client/server views.
- [ ] Help page shows current app version.

## 5. Stability
- [ ] 20-minute soak run with repeated view switching has no crashes.
- [ ] Start/stop server and scanner repeatedly (10x) without leaked state.
- [ ] Serial disconnect/reconnect behavior is safe and recovers.

## 6. Pre-Release Hygiene
- [ ] `git status` is clean.
- [ ] Changelog entry for v1 is complete.
- [ ] Tag release commit (`v1.0.0`) after final verification.
