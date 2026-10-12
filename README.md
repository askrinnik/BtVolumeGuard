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

- If the volume jumps to the maximum in one step (≥ 95%, by at least 20 points), the previous volume is restored.
- When the `Hands-Free` device appears at the start of a call already at 100%, the volume from the previous call is applied (or a default of 20% if none is known).
- The volume set by the user is remembered between calls and across restarts.

It reacts to Windows Core Audio notifications (via [NAudio](https://github.com/naudio/NAudio)) rather than polling, so the correction should be fast enough to go unnoticed.

A working PowerShell prototype of the same idea is in [`prototype/LK12-VolumeGuard.ps1`](prototype/LK12-VolumeGuard.ps1) (polling every 100 ms, verified by simulating a jump; not yet verified on a real call).

## Status

Early development. Roadmap step 1 is done: the tray app watches matching endpoints and logs every volume change with its event context to `%LOCALAPPDATA%\BtVolumeGuard\BtVolumeGuard.log` (tray menu → Open log). It does not revert anything yet. Next steps are in [`docs/design.md`](docs/design.md).

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
