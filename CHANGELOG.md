# Changelog

## v1.0.0 - 2026-05-30

### Added
- WPF application shell with themed navigation and tool workspace.
- Modbus TCP Client and Modbus TCP Server views.
- Modbus RTU Client and Modbus RTU Server views.
- RTU Serial Scanner view for multi-baud/frame slave discovery.
- Console command system with runtime commands (`/help`, `/clear`, `/status`, `/loglevel`, `/packets`, `/regs`, `/sync`).
- Shared runtime/data store model for persistent server state across view switches.
- In-app Help view expanded with quick-start, command reference, and troubleshooting.
- Application icon assets and custom title-bar icon display.
- Basic integration test project with TCP loopback read test.

### Changed
- Migrated active solution target from legacy WinForms UI to WPF UI.
- Grouped Modbus tools under a single navigation subtree.
- Improved scanner stop/cancel reliability and timeout handling.
- Added sparse mapped register dump support for `/regs all`.
- Improved server view synchronization with datastore updates.
- Refined settings/theme behavior to avoid redundant theme application.

### Removed
- Legacy `DevTools.App` WinForms project from solution and repository.
- Obsolete root preset/capture artifacts and stale VS Code launch entries.
