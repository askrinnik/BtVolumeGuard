# BtVolumeGuard

Windows tray app (.NET 10, WinForms) that reverts Bluetooth headset volume jumps to 100% at the start of a call (HFP profile). Problem statement and background: `README.md`. Requirements, decisions, open questions, roadmap: `docs/design.md`. Read both before starting a task.

## Commands

```bash
dotnet build src                              # whole solution (src/BtVolumeGuard.slnx); warnings are errors
dotnet test src                               # xUnit tests for Core
dotnet run --project src/BtVolumeGuard        # run the tray app
dotnet format src --verify-no-changes         # check formatting against src/.editorconfig
```

Before running the tray app, make sure no earlier instance is running (single-instance mutex `Local\BtVolumeGuard`; a second instance exits silently). Stop it with `Stop-Process -Name BtVolumeGuard`.

## Layout

`src/` is a self-contained C# solution: `BtVolumeGuard.slnx` (flat, no solution folders), `Directory.Build.props`, `.editorconfig` and all projects, including tests.

- `src/BtVolumeGuard.Core`: logic and Core Audio access (NAudio.Wasapi). No UI.
  - `VolumeJumpPolicy`: pure decision logic (jump detection, volume on endpoint appearance).
- `src/BtVolumeGuard`: WinExe tray shell (`Program`, `TrayApplicationContext`).
- `src/BtVolumeGuard.Core.Tests`: xUnit tests.
- `prototype/LK12-VolumeGuard.ps1`: working PowerShell prototype (polling, own Core Audio interop). Reference for behavior; not built or shipped.

## Conventions

- All projects target `net10.0-windows`. Shared settings live in `src/Directory.Build.props` (nullable, implicit usings, `TreatWarningsAsErrors`).
- Keep decision logic in pure classes in Core with unit tests; keep NAudio/COM code thin and separate.
- Never set volume or do blocking work inside Core Audio notification callbacks; hand off to another thread.
- Tag the app's own volume changes with its event-context GUID and ignore notifications that carry it.
- Code, comments, commit messages and repository docs in English. Talk to the user in Russian.

## Testing on the real device

Unit tests cannot cover Core Audio or the headset. Behavior that depends on them must be checked on the machine:

- `Headset (LK12 Hands-Free)` exists only during a call; to test, start a Teams meeting (or any app that opens the headset microphone) with the LK12 connected. Only the user can do this; ask them, and say exactly what to look for in the log.
- To test reverting without a call, point the device mask at another endpoint (e.g. `*Realtek*`) and change its volume programmatically. Restore the original volume afterwards.
- Report what was verified on a real call and what was only simulated.

## Do not

- Change system settings, the registry under `HKLM`, drivers or Bluetooth services. This app works only at the user level.
- Register scheduled tasks or autostart entries on the user's machine without asking; the app's own "Start with Windows" menu item is the intended way.
