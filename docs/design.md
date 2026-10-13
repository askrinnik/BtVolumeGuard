# Design

## Requirements

1. Guard every Bluetooth playback endpoint by default (`BluetoothEndpoint`, see "Detecting Bluetooth endpoints"); devices can be excluded by friendly name in the tray menu → Devices, which lists every Bluetooth playback device Windows knows, connected or not (`DeviceSelection`, setting `ExcludedDevices`). On Windows 11 the LK12 has one endpoint for both profiles (`Headphones (LK12)`); a separate Hands-Free endpoint, if it exists on a setup, is guarded the same way. For tests, `--mask` guards endpoints of any kind by name instead.
2. Revert the headset's push to maximum right after a profile switch, and any single-step jump to maximum from an unrecognised source. Never revert user changes (`EndpointGuard.OnChange`; rules in "Guard rules" below).
3. When an endpoint appears (after startup) already at maximum, set the remembered volume, or the default if none is known (`EndpointGuard.OnAppeared`).
4. Remember the user's volume per endpoint (keyed by endpoint ID) and persist it across restarts.
5. Tray-only UI, no main window. No admin rights. Single instance per user session.
6. Log every correction with timestamps. Log level in the settings: `Off`, `Normal` (corrections, endpoints, errors; default), `Verbose` (every volume change with its event context, for diagnostics).

## Facts about the headset

- The LK12 headset is Bluetooth Classic only (profiles HSP, HFP, AVRCP, A2DP; codecs SBC/AAC). No LE Audio.
- **One playback endpoint for both profiles** (observed on a real Teams call, 2026-10-12). No `Headset (LK12 Hands-Free)` endpoint appeared, and no device was added or changed state during the call. A2DP ↔ HFP switching happens inside `Headphones (LK12)` (Windows 11 unified Bluetooth endpoint), so the endpoint ID stays the same. `Headphones (LK12)` also has stale `NotPresent` entries with other IDs. (An earlier observation of a separate `Hands-Free` render endpoint was not reproduced.)
- **Windows keeps a separate volume per profile and remembers both.** At call start Windows switches the endpoint to the HFP volume left from the previous call; at call end it switches back to the A2DP volume from before the call.
- **The jump is a second step, pushed by the headset.** About 0.5 s after the switch to HFP the headset reports maximum volume (HFP `AT+VGS`), and the driver sets 100%. Observed sequences (two runs, 2026-10-12):
  - `27% → 88%` (profile switch; 88% was left from the previous call) → `100%` (driver, +0.6 s);
  - `27% → 58%` (profile switch; 58% was set in the previous call) → `100%` (driver, +0.5 s);
  - `25% → 100%` (profile switch; HFP volume was already 100%), no separate driver step visible.

  So Windows already restores a sensible call volume; the headset overrides it on every call.
- **Event contexts seen on `Headphones (LK12)`:**

  | Context | Source | Notes |
  |---|---|---|
  | `03016827-7b05-566a-ada3-14d84347d041` | Profile switch A2DP ↔ HFP (call start and end) | Sets the volume remembered for the profile. Name-based (v5) GUID, probably a constant of the Windows Bluetooth audio stack; not yet checked across reconnects and reboots. |
  | `0de6c1af-c506-4e60-bd8c-f3b00097de2a` | Bluetooth driver: volume reported by the headset | **The jump to 100%** after the switch to HFP; headset buttons outside a call (steps of 1/16: 6.25%, 12.5%, 18.7%…); a re-quantizing echo right after every Windows change in A2DP. |
  | `00000000-0000-0000-0000-000000000000` | Keyboard volume keys; `SetMasterVolumeLevelScalar` with `Guid.Empty` | 2% steps, in A2DP and HFP. |
  | `355c8163-187b-45d8-9882-61afb3537f61` | Volume slider in the taskbar flyout | 1% steps. Probably owned by the shell process; stability unknown. |

- **Headset buttons during a call produce no notification at all**: pressed several times in a call, nothing was logged.
- **The switch back to A2DP at call end can arrive late.** Once the safety poll saw `50% → 27%` before any notification; the profile-switch notification came 0.6 s later, already carrying 27%. Logic must not assume notification order matches the actual volume.
- Otherwise notifications arrive within milliseconds.

## Detecting Bluetooth endpoints

Observed on the development machine (2026-10-12), `BluetoothEndpoint.IsBluetooth`:

- Classic Bluetooth endpoints have the device enumerator (`PKEY_Device_EnumeratorName`) `BTHENUM` (`BTHHFENUM` for Hands-Free).
- When Bluetooth audio is routed through **Intel Smart Sound Technology for Bluetooth Audio** (as for the LK12 here), the enumerator is `INTELAUDIO`, like the built-in Realtek speakers. The endpoint property `{b3f8fa53-0004-438e-9003-51a46e139bfc},39` (undocumented) then holds the path of the Bluetooth node, e.g. `{1}.BTHENUM{0000110B-0000-1000-8000-00805F9B34FB}_LOCALMFG&...` (`110B` = A2DP sink, `111E` = Hands-Free). Either signal is enough.
- Windows keeps endpoints of earlier pairings (`NotPresent`), several per headset, with the same friendly name. The tray menu therefore lists devices by friendly name, and exclusions are stored by name.

## Design decisions

- **NAudio** (`NAudio.Wasapi`) for Core Audio: `MMDeviceEnumerator`, `IMMNotificationClient` (device added / state changed / default changed), `AudioEndpointVolume.OnVolumeNotification`.
- **Event-driven, with a safety poll.** React to notifications for low latency; additionally poll every 1–2 s in case a notification is missed.
- **Do not set volume inside the notification callback.** Core Audio callbacks must not block; marshal the work to a dedicated worker thread or the UI thread.
- **Ignore our own changes.** Every `SetMasterVolumeLevelScalar` passes the app's own event-context GUID; notifications carrying that GUID are skipped.
- **Pure logic in Core.** `VolumeJumpPolicy` and `EndpointGuard` have no Windows API dependencies and is covered by unit tests. Keep it that way for any new decision logic.

## Open question: who changed the volume?

`AudioVolumeNotificationData` carries an event-context GUID. If changes coming from the headset (HFP `AT+VGS` via the driver) carry a context different from changes made by the Windows volume slider or the keyboard, the guard can revert **only** headset-initiated changes. That would remove the main limitation of the prototype: a deliberate fast move to 100% by the user also gets reverted.

**Answer (two real Teams sessions, 2026-10-12): yes.** User changes (keyboard `Guid.Empty`, flyout slider `355c8163-…`) never share a context with the jump. The jump comes from the driver (`0de6c1af-…`), right after a profile switch (`03016827-…`) to HFP. The driver context alone is not enough (headset buttons outside a call use it too), but the sequence is distinctive: *profile switch, then within about a second a driver change to maximum*.

Still to check:

- Are `03016827-…` and `0de6c1af-…` stable across reboots? **Stable across headset reconnects** (checked 2026-10-12: headset off and on twice, then a call: same contexts, jump reverted 526 ms after the switch). Reboot not checked yet. If they change, the fallback rule below (single-step jump from an unknown source) still catches large jumps, but not a small one such as 88% → 100%.

## Guard rules

Implemented in `EndpointGuard` (pure, unit-tested); `VolumeChangeContexts` maps contexts to sources. "Jump" means: new volume ≥ max (95%), previous below max.

| Source | Rule |
|---|---|
| `User` (`Guid.Empty`, taskbar slider) | Always accepted, including moves to 100%. |
| `ProfileSwitch` | Accepted, unless it is a single-step jump of ≥ 20 points (the remembered call volume is 100%): then reverted to the previous volume. Starts the profile switch window. |
| `HeadsetDriver` within 2 s after a profile switch | Any jump is reverted to the volume before it (the call volume Windows just set). |
| `HeadsetDriver` otherwise (headset buttons) | Accepted, unless it is a single-step jump of ≥ 20 points (buttons move in 1/16 steps). |
| `Unknown` context, or a change found by polling | Prototype rule: a single-step jump of ≥ 20 points is reverted. |
| `Own` | Ignored; only updates the known volume. |

Once the guard sets the call volume during a call, Windows remembers it for the next call, so later calls start from it.

Known limitation: if the user's *music* volume is at maximum and the call volume is below it, the profile switch at the end of a call is a jump of ≥ 20 points and is reverted to the call volume. The guard cannot tell the switch direction yet.

The safety poll reports a volume that differs from the last notification only when it sees it twice in a row: a late notification carries the context the rules need.

## Roadmap

1. **Core Audio layer:** enumerate matching endpoints, subscribe to volume and device notifications, log changes with event context. Diagnostic log only, no reverting yet. *Done.*
2. **Verify on a real Teams call** and resolve the open question above. *Done* 2026-10-12: the jump has its own event context (see above); a few follow-up checks remain.
3. **Guard:** wire the decision logic into the event flow, revert jumps, remember volumes. *Done* (`EndpointGuard`, `Audio/EndpointVolumeGuard`); remembered volumes are in memory only. Verified on two real Teams calls (2026-10-12): the push to 100% came 500 and 639 ms after the profile switch and was reverted to the call volume (50%, then 68% set in the previous call) within about 50 ms; user changes during and after the calls were left alone. Contexts were the same as in the earlier sessions (same boot).
4. **Settings and state** next to the executable (JSON): device mask, thresholds, default volume, remembered volumes per endpoint ID. Log file in the same folder. *Done:* `BtVolumeGuard.settings.json` (`AppSettings`; read at startup, created with defaults if missing, comments allowed) and `BtVolumeGuard.state.json` (`VolumeMemory`; saved on every change of a remembered volume). Changing settings requires a restart.
5. **Tray menu:** enable/disable guard, current headset volume, default volume, open log, "Start with Windows" (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), Exit. Optional balloon notification on revert. Proper icon. *Done:* menu with the current volume of watched endpoints, "Guard enabled", "Notify when a jump is reverted" (both saved to the settings file), "Start with Windows", "Log" (submenu: level Off / Normal / Verbose, "Open log", "Clear log"), "Open settings", "Reload settings", "Exit". A left click on the icon opens the menu too. The default volume is edited in the settings file. Tray icon: headset glyph with a shield, drawn at runtime (`TrayIconRenderer`), white or black to match the taskbar theme, green shield when the guard is on, grey when off; the tooltip shows the state and volumes. Saving from the menu rewrites the settings file, so comments in it are lost.
6. **Publish:** framework-dependent `win-x64`, copied to any user-writable folder (not `Program Files`, not `%LOCALAPPDATA%`). Settings, state and log stay next to the executable, so the folder must be writable. *Done:* `tools/publish.ps1` builds a single-file exe (~1.5 MB, needs the .NET 10 Desktop Runtime) and with `-InstallDir` installs or updates it (refuses Program Files, stops a running copy, restarts it if it ran from that folder, keeps settings/state/log). Exe icon `BtVolumeGuard.ico` (dark headset with a white outline, visible on light and dark backgrounds) generated by `tools/make-icon.ps1`. Verified by installing into a temporary folder, simulating a jump on a Realtek endpoint and reinstalling over the running copy.
7. Retire the PowerShell prototype once the app is verified.
8. **Any Bluetooth headset:** guard all Bluetooth playback devices by default, Devices submenu (not connected ones marked "(offline)"), event contexts in the settings, README for Bluetooth headsets in general with the LK12 as the verified example. *Done* (2026-10-12); device detection verified on the development machine (LK12, G2, JBL Flip 4, A2 found; Realtek, HDMI, USB not), the jump revert re-checked on a Realtek endpoint with `--mask`; not yet with a headset other than the LK12.
