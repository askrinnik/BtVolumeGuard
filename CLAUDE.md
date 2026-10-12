# BtVolumeGuard

Windows tray app (.NET 10, WinForms) that reverts Bluetooth headset volume jumps to 100% at the start of a call (HFP profile). Problem statement and background: `README.md`. Requirements, decisions, open questions, roadmap: `docs/design.md`. Read both before starting a task.

## Commands

```bash
dotnet build src                              # whole solution (src/BtVolumeGuard.slnx); warnings are errors
dotnet test src                               # xUnit tests for Core
dotnet run --project src/BtVolumeGuard        # run the tray app
dotnet format src --verify-no-changes         # check formatting against src/.editorconfig
.\tools\publish.ps1 [-InstallDir <folder>]    # release single-file exe to publish\ (PowerShell 7)
.\tools\make-icon.ps1                         # regenerate src/BtVolumeGuard/BtVolumeGuard.ico (PowerShell 7)
```

Before running the tray app, make sure no earlier instance is running (single-instance mutex `Local\BtVolumeGuard`; a second instance exits silently). Stop it with `Stop-Process -Name BtVolumeGuard`.

## Layout

`src/` is a self-contained C# solution: `BtVolumeGuard.slnx` (flat, no solution folders), `Directory.Build.props`, `.editorconfig` and all projects, including tests.

- `src/BtVolumeGuard.Core`: logic and Core Audio access (NAudio.Wasapi). No UI.
  - `VolumeJumpPolicy`: thresholds and stateless jump rules.
  - `EndpointGuard`: per-endpoint guard state; decides accept / revert / ignore for each change (pure, time passed in).
  - `VolumeChangeContexts`: maps observed event-context GUIDs to `VolumeChangeSource`; holds the app's own GUID.
  - `DeviceNameMask`, `VolumeLevel`: pure helpers (wildcard name match, scalar ↔ percent).
  - `Audio/EndpointVolumeGuard`: owns all NAudio objects on one worker thread; feeds changes to `EndpointGuard`, sets volume with the own context, logs reverted jumps (every change at `Verbose`).
  - `Logging/FileLog`: timestamped log, `BtVolumeGuard.log` next to the executable. Levels `Off`, `Normal` (start/stop, endpoints, reverted jumps, errors), `Verbose` (also every volume change with its event context); `LogLevel` in the settings, switchable from the tray menu.
  - `Settings/AppSettings`: `BtVolumeGuard.settings.json` next to the executable (created with defaults if missing; invalid file → defaults + log line).
  - `Settings/VolumeMemory`: `BtVolumeGuard.state.json`, last volume below maximum per endpoint ID.
- `src/BtVolumeGuard`: WinExe tray shell: `Program`, `TrayApplicationContext` (menu, tooltip, notifications), `TrayIconRenderer` (icon drawn at runtime), `StartupRegistration` (HKCU Run value, changed only from the menu). `--mask "<pattern>"` overrides the device mask from the settings file (default `*LK12*`).
- `src/BtVolumeGuard.Core.Tests`: xUnit tests.
- `tools/`: `publish.ps1` (framework-dependent single-file win-x64; with `-InstallDir` copies the exe, stopping and restarting a running copy), `make-icon.ps1` (draws the exe icon with `TrayIconRenderer`, style `DarkWithHalo`).
- `prototype/LK12-VolumeGuard.ps1`: working PowerShell prototype (polling, own Core Audio interop). Reference for behavior; not built or shipped.

## Conventions

- All projects target `net10.0-windows`. Shared settings live in `src/Directory.Build.props` (nullable, implicit usings, `TreatWarningsAsErrors`).
- Keep decision logic in pure classes in Core with unit tests; keep NAudio/COM code thin and separate.
- Never set volume or do blocking work inside Core Audio notification callbacks; hand off to another thread.
- Tag the app's own volume changes with its event-context GUID and ignore notifications that carry it.
- Code, comments, commit messages and repository docs in English. Talk to the user in Russian.

## Testing on the real device

Unit tests cannot cover Core Audio or the headset. Behavior that depends on them must be checked on the machine:

- The jump happens only on a call: the headset switches to HFP inside `Headphones (LK12)` (no separate Hands-Free endpoint on this machine). To test, start a Teams meeting (or any app that opens the headset microphone) with the LK12 connected. Only the user can do this; ask them, and say exactly what to look for in the log.
- To test reverting without a call, point the device mask at another endpoint (e.g. `*Realtek*`) and change its volume programmatically. Restore the original volume afterwards.
- To read event-context GUIDs from a real call, ask the user to set the log level to `Verbose` (tray menu → Log) first; at `Normal` volume changes are not logged.
- Report what was verified on a real call and what was only simulated.

## Do not

- Change system settings, the registry under `HKLM`, drivers or Bluetooth services. This app works only at the user level.
- Register scheduled tasks or autostart entries on the user's machine without asking; the app's own "Start with Windows" menu item is the intended way.
