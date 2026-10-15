# Implementation notes

## Input and gestures

`Program.cs` creates a message-only Raw Input window and registers keyboard and consumer-control input with input-sink/device-notification flags. It filters by the configured VID/PID/interface and interprets F13 down/up. Repeated downs are ignored. Rotation cancels an unfinished gesture; disconnect resets state. No global F13 hotkey is reserved and normal keyboard input is not logged. Applications can still receive the F13 event.

`KnobInput.cs` adds an opt-in 2.4 GHz receiver mode. The tested receiver has the same VID/PID/interface as the wired target. F13/F12 mappings persisted in vendor configuration but produced no keyboard reports. Media → Treble + produced the 3-byte consumer reports `05 53 00` on press and `05 00 00` on release, preserving physical hold duration. These exact observed packets feed the existing gesture detector. Volume input still cancels pending gestures; malformed reports and vendor configuration replies are ignored. This measured mapping is specific to the tested revision, rather than a claim that every device encodes Treble + as usage 0x0053. The app does not suppress original consumer input from other applications. Existing profiles retain F13 mode, and receiver mode is saved independently of actions and timing.

`ClickDetector.cs` is an isolated monotonic-millisecond state machine. A release increments the tap count and starts the corresponding next-press deadline. The next press pauses the deadline while the button is down. A third release commits triple immediately. A 15ms UI timer checks hold duration, emits once at the threshold and consumes subsequent repeats/release. Under system load actual dispatch may occur later than a timer interval; it is not a real-time guarantee.

The WPF settings window runs alongside the WinForms tray/message loop using modeless keyboard interop. Draft settings preview immediately; Save commits atomically with a previous-version backup. Closing the window restores the saved configuration and enables actions. Language and autostart preferences commit immediately. A named mutex prevents competing app instances; subsequent launches signal the running instance to show settings.

## Native audio switching

Playback endpoint discovery and default-output selection use Windows COM interfaces directly.

1. `IMMDeviceEnumerator::EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)` lists active playback endpoints. Names come from `PKEY_Device_FriendlyName`; stable endpoint IDs are saved locally.
2. `GetDefaultAudioEndpoint(eRender, eMultimedia)` identifies the current cycle position.
3. `DeviceCycle.Next` filters the user's order to available devices and wraps to the next. Fewer than two available selections reports an error rather than choosing an unselected output.
4. `IPolicyConfig::SetDefaultEndpoint` sets console and multimedia roles, plus communications if enabled. Default roles are read back; if switching fails, previous roles are restored on a best-effort basis.
5. COM objects and property variants are released. Each operation creates its own COM objects, avoiding cached cross-thread RCWs. Real gesture actions run on the serialized background worker.

Windows documents endpoint enumeration in [MMDevice API](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-enumaudioendpoints). The PolicyConfig default-changing method is undocumented; Windows 10 is the validated platform. The application does not override per-app pinned output endpoints.

## Calibration

Labeled trials collect the intended gesture separately from the detector's observed result. First-input time excludes the delay between clicking a trial button and touching the knob. Suggestions use only complete labeled `Source=VK01` traces with the expected number of presses. Misclassified but complete traces can still inform the intended timing. Screen practice is explicitly excluded.

The suggestion heuristic uses the nearest-rank 95th-percentile gap +60ms, bounded to 180–700ms. Hold starts from the 95th-percentile tap +200ms, bounded to 350–1500ms. If labeled hold times overlap this candidate it checks for separation and may choose a midpoint; overlapping tap/hold samples are rejected. These are conservative application heuristics, not standards or device latency measurements.

## Persistence and startup

All user state lives in `%LOCALAPPDATA%\VK01Studio`, outside the executable/repository:

- `settings.json`, previous `settings.json.bak`
- `measurements.json`, up to 80 recent records
- `activity.log`, rotated at approximately 1MiB

Autostart uses a per-user Run entry named `VK01ClickControl` (retained for migration compatibility) targeting the current executable with `--background`. It requires no administrator access. `--install-startup` and `--remove-startup` are also supported. Exporting the executable does not export endpoint IDs or measurement history.

## Validation

`scripts/build.ps1 -Test` compiles the app and runs pure tests for exclusive gestures, hold auto-trigger/repeat suppression, rotation/disconnect, independent windows, legacy configuration, atomic save, calibration filtering, history serialization and output order/disconnect/wrap behavior.

Hardware validation on Windows 10: VK01 presence, F13 input, both active Realtek/iFi endpoints, direct output change/readback/restore, communications role excluded, startup registry toggle and immediate Korean/English switching. The supplied 2.4 GHz receiver was additionally verified for physical single, double, triple and hold detection using the Treble + mapping, including threshold activation before release without a second gesture. Input tests cover report filtering, mode isolation and receiver gesture sequences. UI verification covers navigation, actual native switching, dropdowns, draft/save/revert and layout. Windows 11, alternate hardware revisions and direct Bluetooth pairing remain unverified.

Run optional audio integration checks locally:

```powershell
.\build\AudioTests.exe --integration
# This deliberately switches output and then restores it:
.\build\AudioTests.exe --switch-roundtrip
```
