# BtVolumeGuard

A Windows tray app that stops Bluetooth headsets from jumping to 100% volume when a call starts.

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

A working PowerShell prototype of the same idea is in [`prototype/LK12-VolumeGuard.ps1`](prototype/LK12-VolumeGuard.ps1) (polling every 100 ms, verified by simulating a jump; not yet verified on a real call).

## Status

Early development. The guard (roadmap step 3) is implemented: the tray app reverts the jump and logs every volume change, its event context and the decision to `BtVolumeGuard.log` next to the executable (tray menu → Open log). Verified on real Teams calls. Settings and remembered volumes are stored as JSON next to the executable (`BtVolumeGuard.settings.json`, `BtVolumeGuard.state.json`). The full tray menu and an icon are next; see [`docs/design.md`](docs/design.md).

## Build and run

Requires the .NET 10 SDK, Windows 10/11.

```bash
dotnet build src
dotnet test src
dotnet run --project src/BtVolumeGuard
```

## Repository layout

```
src/                           Self-contained C# solution: BtVolumeGuard.slnx, Directory.Build.props, .editorconfig
src/BtVolumeGuard.Core/        Logic and Core Audio access (NAudio), no UI
src/BtVolumeGuard/             WinForms tray app (WinExe)
src/BtVolumeGuard.Core.Tests/  xUnit tests for Core
prototype/                     PowerShell prototype
docs/design.md                 Requirements, design decisions, roadmap
```
