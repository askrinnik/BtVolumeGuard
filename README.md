<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/banner-dark.png">
    <img src="docs/banner-light.png" width="800" alt="BtVolumeGuard: a Windows tray app that stops Bluetooth headsets from jumping to 100% volume when a call starts">
  </picture>
</p>

[![CI](https://github.com/askrinnik/BtVolumeGuard/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/askrinnik/BtVolumeGuard/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/askrinnik/BtVolumeGuard)](https://github.com/askrinnik/BtVolumeGuard/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## The problem

With the **LK12** open-ear Bluetooth headset (on any Windows 11 machine), the playback volume is set to, say, 20%, but every time a Microsoft Teams meeting starts, the volume jumps to **100%**. It happens again on every new meeting, so the volume has to be turned down by hand each time.

### Why it happens

A classic Bluetooth headset exposes two audio profiles, which Windows shows as separate playback devices:

| Windows device | Bluetooth profile | When it is used |
|---|---|---|
| `Headphones (LK12)` | A2DP (stereo) | Music, video, no microphone |
| `Headset (LK12 Hands-Free)` | HFP (Hands-Free) | Calls: whenever an app opens the headset microphone |

When Teams opens the microphone, Windows switches the headset to HFP. The `Hands-Free` endpoint exists only during a call. On the HFP connection the headset reports **maximum volume** to Windows (HFP volume sync, `AT+VGS`), and Windows applies it to the endpoint.

### What did not help

- **Disabling Bluetooth Absolute Volume** (`HKLM\SYSTEM\ControlSet001\Control\Bluetooth\Audio\AVRCP\CT\DisableAbsoluteVolume = 1`). The jump still happened, because Absolute Volume covers AVRCP/A2DP, not HFP. Also Windows could no longer control Bluetooth volume, only the headset buttons could. Reverted.
- **The headset manual.** It has no "remember volume" or similar setting, only volume buttons and a factory reset.
- **Avoiding HFP**: using the stereo device in Teams with another microphone, or disabling the Hands-Free service. This removes the jump, but the whole point of the headset is its directional, noise-rejecting microphone, so this is not acceptable.

The jump is initiated by the headset itself, so Windows settings cannot prevent it. What remains is to **detect the jump and revert it immediately**.

## The solution

BtVolumeGuard runs in the system tray and watches the volume of the selected Bluetooth playback devices (name mask, `*LK12*` by default):

- At the start of a call Windows switches the headset to the call profile and sets the call volume it remembered; about half a second later the headset pushes 100%. BtVolumeGuard recognises that push by its Core Audio event context and restores the volume Windows had set.
- Any other single-step jump to the maximum (≥ 95%, by at least 20 points) from an unrecognised source is reverted too.
- Changes made by the user (keyboard, volume sliders, headset buttons) are never reverted, even a deliberate move to 100%.
- If a separate `Hands-Free` device appears already at 100% (seen on other setups), the last known volume or a default of 20% is applied.

It reacts to Windows Core Audio notifications (via [NAudio](https://github.com/naudio/NAudio)) rather than polling, so the correction should be fast enough to go unnoticed. How the jump was analysed: [`docs/design.md`](docs/design.md).

Everything is in the tray menu (left or right click on the icon): the current headset volume, turning the guard and revert notifications on or off, starting with Windows, the log level, and the log and settings files. The shield in the icon turns grey while the guard is off.

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/tray-menu.png">
    <img src="docs/screenshots/tray-menu-light.png" alt="Tray menu: Headphones (LK12): 36%, Guard enabled, Notify when a jump is reverted, Start with Windows, Log submenu (Off / Normal / Verbose, Open log, Clear log), Open settings, Reload settings, Exit">
  </picture>
</p>

A working PowerShell prototype of the same idea is in [`prototype/LK12-VolumeGuard.ps1`](prototype/LK12-VolumeGuard.ps1) (polling every 100 ms, verified by simulating a jump; not yet verified on a real call).

## Status

Version 0.1.0, in daily use. Roadmap steps 1–6 are done (see [`docs/design.md`](docs/design.md)):

- The tray app reverts the jump at the start of a call. Verified on real Teams calls, also after the headset was switched off and on again.
- Reverted jumps are logged to `BtVolumeGuard.log` next to the executable (tray menu → Log: level, open, clear; level `Off`, `Normal` or `Verbose`, where `Verbose` also records every volume change with its event context).
- Settings and remembered volumes are stored as JSON next to the executable (`BtVolumeGuard.settings.json`, `BtVolumeGuard.state.json`).
- The tray menu shows the current headset volume and lets you turn the guard and revert notifications on or off, start the app with Windows, open the settings and reload them.
- It is published as a single-file exe; see [Install](#install).

Still open: whether the event contexts the guard relies on stay the same after a reboot, and retiring the PowerShell prototype (roadmap step 7).

## Build and run

Requires the .NET 10 SDK, Windows 10/11.

```bash
dotnet build src
dotnet test src
dotnet run --project src/BtVolumeGuard
```

## Install

The app is a single `BtVolumeGuard.exe` (framework-dependent, needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)). It keeps its settings, remembered volumes and log next to itself, so put it in a folder you can write to (not `Program Files`).

```powershell
.\tools\publish.ps1                                   # builds publish\BtVolumeGuard.exe
.\tools\publish.ps1 -InstallDir D:\Tools\BtVolumeGuard  # also copies it there (stops and restarts a running copy)
```

Ready-made builds: every CI run attaches `BtVolumeGuard.exe` as an artifact, and pushing a tag `v<version>` (e.g. `v0.1.1`) creates a [GitHub Release](https://github.com/askrinnik/BtVolumeGuard/releases) with the exe built for that version.

Start the installed exe once and use the tray menu → "Start with Windows" to run it at logon. Updating is the same `-InstallDir` command; settings and the log are kept.

## Repository layout

```
src/                           Self-contained C# solution: BtVolumeGuard.slnx, Directory.Build.props, .editorconfig
src/BtVolumeGuard.Core/        Logic and Core Audio access (NAudio), no UI
src/BtVolumeGuard/             WinForms tray app (WinExe)
src/BtVolumeGuard.Core.Tests/  xUnit tests for Core
.github/workflows/ci.yml       CI: build, tests, format check, publish; release on tag v*
tools/publish.ps1              Release build and install into a folder
tools/make-icon.ps1            Regenerates the executable's icon
prototype/                     PowerShell prototype
docs/design.md                 Requirements, design decisions, roadmap
```

## License

[MIT](LICENSE)
