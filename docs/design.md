# Design

## Requirements

1. Guard playback endpoints whose name matches a configurable mask (default `*LK12*`). Both endpoints of the headset are guarded: `Headphones (LK12)` (A2DP) and `Headset (LK12 Hands-Free)` (HFP).
2. Revert a single-step jump to maximum (`VolumeJumpPolicy.OnVolumeChanged`).
3. When an endpoint becomes active already at maximum, set the remembered volume, or the default if none is known (`VolumeJumpPolicy.OnEndpointAppeared`).
4. Remember the user's volume per endpoint (keyed by endpoint ID) and persist it across restarts.
5. Tray-only UI, no main window. No admin rights. Single instance per user session.
6. Log every correction with timestamps.

## Facts about the headset

- The LK12 headset is Bluetooth Classic only (profiles HSP, HFP, AVRCP, A2DP; codecs SBC/AAC). No LE Audio.
- `Headset (LK12 Hands-Free)` is **not** listed among active render endpoints outside of a call; it appears when a call opens the headset microphone. `Headset (LK12)` is the HFP **capture** endpoint (the microphone), not a playback device.
- Outside of a call, `Headset (LK12 Hands-Free)` is not enumerated as a render endpoint in **any** state (not even `NotPresent`), while `Headphones (LK12)` has one `Active` and several stale `NotPresent` entries with different IDs (observed 2026-10-12).
- Changes made through `SetMasterVolumeLevelScalar` with `Guid.Empty` (as the prototype does) arrive with event context `Guid.Empty`.

## Design decisions

- **NAudio** (`NAudio.Wasapi`) for Core Audio: `MMDeviceEnumerator`, `IMMNotificationClient` (device added / state changed / default changed), `AudioEndpointVolume.OnVolumeNotification`.
- **Event-driven, with a safety poll.** React to notifications for low latency; additionally poll every 1–2 s in case a notification is missed.
- **Do not set volume inside the notification callback.** Core Audio callbacks must not block; marshal the work to a dedicated worker thread or the UI thread.
- **Ignore our own changes.** Every `SetMasterVolumeLevelScalar` passes the app's own event-context GUID; notifications carrying that GUID are skipped.
- **Pure logic in Core.** `VolumeJumpPolicy` has no Windows API dependencies and is covered by unit tests. Keep it that way for any new decision logic.

## Open question: who changed the volume?

`AudioVolumeNotificationData` carries an event-context GUID. If changes coming from the headset (HFP `AT+VGS` via the driver) carry a context different from changes made by the Windows volume slider or the keyboard, the guard can revert **only** headset-initiated changes. That would remove the main limitation of the prototype: a deliberate fast move to 100% by the user also gets reverted.

First milestone must log the event context for every change, then verify on a real Teams call:

1. Change volume with the Windows slider → note the GUID.
2. Change volume with the headset buttons → note the GUID.
3. Start a Teams meeting (the jump) → note the GUID.

## Roadmap

1. **Core Audio layer:** enumerate matching endpoints, subscribe to volume and device notifications, log changes with event context. Diagnostic log only, no reverting yet. *Done* (`EndpointVolumeMonitor`); verified with a simulated change on a Realtek endpoint, not yet on a call.
2. **Verify on a real Teams call** and resolve the open question above.
3. **Guard:** wire `VolumeJumpPolicy` into the event flow, revert jumps, remember volumes.
4. **Settings and state** in `%LOCALAPPDATA%\BtVolumeGuard\` (JSON): device mask, thresholds, default volume, remembered volumes per endpoint ID. Log file in the same folder.
5. **Tray menu:** enable/disable guard, current headset volume, default volume, open log, "Start with Windows" (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), Exit. Optional balloon notification on revert. Proper icon.
6. **Publish:** framework-dependent `win-x64`, install to `%LOCALAPPDATA%\Programs\BtVolumeGuard\`.
7. Retire the PowerShell prototype once the app is verified.
