# Emergency Manager and Low-Bandwidth Remote Control Design

Date: 2026-09-23

## Summary

This fork adds a Windows tray manager for on-demand control of the existing `Remotely_Service` and adds runtime-adjustable low-bandwidth settings for active remote-control sessions.

The design intentionally preserves Remotely's existing server, authentication, SignalR, Long Polling, Resident Agent session flow, and unattended-control flow. The fork changes only the Windows client-side operational experience and desktop-stream tuning.

The primary use case is occasional or emergency remote access. The Resident Agent should not remain online continuously when the user does not need remote access.

## Goals

1. Provide a Windows tray manager that acts as the user's explicit remote-access switch.
2. Starting the tray manager automatically starts `Remotely_Service` if it is stopped.
3. The tray icon always reflects the real Windows service state.
4. The user can start, stop, and inspect `Remotely_Service` from the tray menu.
5. Exiting the tray manager stops `Remotely_Service` before the manager exits.
6. Install/configure the Resident Agent service as `Manual` by default for this fork so it is not permanently running.
7. Add graphical remote-quality settings.
8. Preserve original Remotely quality behavior as the default profile.
9. Add three lower-bandwidth presets plus a custom mode.
10. Changes to quality/FPS/audio should apply to an already-running remote-control session without reconnecting.
11. Persist the selected profile across manager restarts.

## Non-goals

The first version will not:

- change the CloudBase/server deployment architecture;
- change SignalR transport selection or Long Polling behavior;
- add WebRTC or peer-to-peer transport;
- alter authentication, session IDs, access keys, organization IDs, or server verification tokens;
- replace `ConnectionInfo.json`;
- automatically change Windows display resolution;
- add a separate cloud control plane;
- require the tray manager to remain running after the user deliberately exits it.

## Existing Behavior to Preserve

The Resident Agent remains a Windows service and continues to use Remotely's existing `ConnectionInfo.json` for server/organization/device identity.

When the Remotely server requests unattended control, the Resident Agent continues to launch the Desktop executable in Unattended mode using the existing server URL, session ID, access key, requester information, and organization information.

The server-side `Enforce Attended Access` behavior remains unchanged. This fork does not bypass or replace Remotely's normal authorization flow.

## Component Design

### 1. Remotely Manager

Add a lightweight Windows tray application, tentatively named `Remotely_Manager.exe`.

The manager is the user's operational control surface for the Resident Agent service and remote-stream settings.

Preferred implementation stack: .NET 8 with Avalonia, reusing the repository's existing UI technology where practical.

The manager should be a separate executable rather than merging tray behavior into the Windows service. This keeps interactive user UI out of Session 0 and avoids coupling Windows service lifetime to the logged-in desktop process.

### 2. Remotely Service

The existing Windows service name remains:

`Remotely_Service`

For this fork, installation should set its startup type to `Manual` rather than `Automatic`.

The tray manager controls this service but does not replace it.

### 3. Shared Emergency Settings

Use a machine-level settings file so both an elevated tray manager and Desktop processes started for unattended sessions can access the same configuration.

Proposed path:

`%ProgramData%\Remotely\EmergencySettings.json`

This file is separate from `ConnectionInfo.json`.

`ConnectionInfo.json` remains responsible for connectivity and device identity. `EmergencySettings.json` contains only local user experience and stream-tuning preferences.

## Tray Lifecycle and State Machine

### Manager startup

On launch:

1. Acquire required elevation once for the manager lifetime.
2. Locate `Remotely_Service`.
3. If the service is `Stopped`, request start.
4. If the service is already `Running`, leave it running.
5. Reflect the actual service state in the tray icon.
6. Load persisted remote-quality settings.

The manager should not maintain a separate optimistic service-state flag. It should query the Windows Service Control Manager and derive UI state from the actual service status.

### Tray service-state presentation

Recommended visual mapping:

- Color icon: `Running`
- Gray icon: `Stopped`
- Yellow icon: `StartPending` or `StopPending`
- Red/error icon: service missing, access denied, timeout, or service operation failure

The manager should periodically refresh state and should also refresh immediately after a start/stop command completes.

External service changes must be reflected. For example, if the service is stopped from Services.msc or PowerShell, the tray icon should turn gray without requiring a manager restart.

### Tray menu

Initial menu:

- `Service: Running` / `Service: Stopped` (status, non-action or disabled)
- `Start Service`
- `Stop Service`
- `Settings...`
- `Exit`

Actions that do not apply to the current state should be disabled where practical.

### Exit semantics

`Exit` means "disable remote access and close the manager."

Sequence:

1. Request `Remotely_Service` stop if it is running or pending.
2. Wait for `Stopped` with a bounded timeout.
3. If stop succeeds, remove tray icon and exit.
4. If stop fails or times out, show an explicit error and do not silently claim that remote access is disabled.

The user must be able to distinguish "manager exited" from "service confirmed stopped." The manager should prefer failing visibly over exiting while leaving the service unknowingly active.

OS shutdown/logoff handling should make a best-effort service stop, while accepting that Windows may impose a short shutdown deadline.

## Permissions Model

Starting/stopping a Windows service normally requires elevated rights.

The preferred first-version design is:

- launch the manager elevated once;
- keep the elevated manager running in the user session;
- avoid a UAC prompt for every Start/Stop action.

Do not weaken the Windows service ACL as a shortcut in V1. A future version can consider a least-privilege helper/service ACL design if the one-time UAC prompt proves too intrusive.

## Remote Quality Configuration

### Profiles

The default profile must preserve original Remotely behavior.

| Profile | JPEG Quality | FPS | Audio | Semantics |
| --- | ---: | ---: | --- | --- |
| Default / Original | 80 | Original behavior | Original behavior | Default and restore-default target |
| Balanced | 65 | 15 | Off | Moderate bandwidth saving |
| Emergency | 45 | 8 | Off | Strong bandwidth saving |
| Ultra Low | 30 | 5 | Off | Maximum bandwidth reduction |
| Custom | User value | User value | User value | Manual tuning |

Important: `Default / Original` must not invent an FPS cap or audio policy that upstream Remotely does not currently impose. In original mode, only behavior already present upstream should apply.

### Initial state

If no emergency settings file exists, behavior is `Default / Original`.

`Restore Defaults` returns to `Default / Original`, not to the Emergency profile.

If the user selects another profile, that profile persists and remains selected the next time the manager starts.

### Settings UI

Recommended V1 controls:

- Preset dropdown: Default / Original, Balanced, Emergency, Ultra Low, Custom
- Image Quality slider/value
- Max FPS slider/value
- Enable Audio checkbox
- Restore Defaults button
- Save/Apply button

Selecting a preset loads its values.

Changing a preset-derived value converts the profile to `Custom`.

In `Default / Original`, UI should clearly communicate that FPS and audio use upstream/original behavior instead of displaying guessed values.

Suggested manual ranges for V1:

- Image Quality: 20-90
- Max FPS: 2-30

These ranges can be refined during implementation if existing capture constraints indicate better bounds.

## Settings Schema

The schema should explicitly distinguish original behavior from an imposed numeric cap.

Example:

```json
{
  "version": 1,
  "profile": "Emergency",
  "imageQuality": 45,
  "maxFps": 8,
  "audioMode": "Off"
}
```

For the original profile, values that mean "use upstream behavior" should be represented explicitly rather than through magic numbers, for example:

```json
{
  "version": 1,
  "profile": "Original",
  "imageQuality": 80,
  "maxFps": null,
  "audioMode": "Original"
}
```

The settings loader should:

- validate bounds;
- tolerate a missing file;
- tolerate unknown future fields;
- fall back safely to `Default / Original` for corrupt or invalid files;
- write updates atomically (temporary file + replace/move) to avoid partially written JSON being observed by Desktop.

## Runtime Application to Active Sessions

The user chose immediate application (Option A): changing settings during a current remote session should affect the live session without disconnect/reconnect.

### Mechanism

Use a lightweight shared-file observation model:

1. Manager writes `EmergencySettings.json` atomically.
2. Desktop-side settings provider observes the settings file for changes.
3. A change is debounced to avoid applying every temporary filesystem event.
4. The provider validates and atomically swaps the current in-memory settings snapshot.
5. The capture/encoding loop reads the current snapshot or receives a settings-changed event.
6. The next eligible encoded frame uses the new quality/FPS/audio behavior.

Target user-visible propagation time: under 1 second under normal conditions.

The implementation should not require the Resident Agent service to restart.

### JPEG quality

Upstream currently uses a default image quality of 80. In this fork, the active setting becomes the quality target/ceiling when a lower-bandwidth profile or Custom mode is selected.

Existing auto-quality behavior must not silently climb a user-selected quality such as 45 back toward 80.

In `Default / Original`, preserve the existing upstream auto-quality semantics.

### FPS limiting

In `Default / Original`, preserve upstream timing behavior.

For profiles with a configured `maxFps`, add a capture/send timing gate that limits changed-frame transmission to the configured maximum without introducing unnecessary latency into input/control messages.

The FPS limiter must affect desktop image frames only, not keyboard/mouse/control DTO handling.

### Audio

In `Default / Original`, preserve upstream audio behavior.

For profiles with audio `Off`, audio capture/sending should be suppressed using the narrowest safe integration point found during implementation.

Changing audio mode should apply to an active session if the underlying audio capturer supports safe start/stop. If investigation shows that runtime re-enable is unsafe in the current architecture, implementation must stop and return for design review rather than silently changing the approved semantics.

## Interaction With Unattended Sessions

No changes are required to the normal server-address or unattended-consent flow.

The correct operational workflow remains:

1. User launches `Remotely_Manager.exe` on the personal PC.
2. Manager starts `Remotely_Service`.
3. Resident Agent connects to the configured Remotely server using its existing `ConnectionInfo.json`.
4. The device becomes Online in the Remotely web UI.
5. Browser user starts Remote Control from Devices.
6. Resident Agent launches the existing Desktop executable in Unattended mode.
7. Desktop reads current emergency settings and applies them.
8. If the user changes quality while connected, Desktop applies the new values live.
9. User exits the manager when finished.
10. Manager confirms `Remotely_Service` has stopped and exits.

## Packaging and Installation

The custom Windows package should include the manager alongside the existing Resident Agent/Desktop payload.

The installer should:

- install/update the existing Remotely binaries as before;
- install `Remotely_Manager.exe`;
- create `Remotely_Service` with startup type `Manual` for this fork;
- preserve `ConnectionInfo.json` on updates as upstream currently does;
- preserve `EmergencySettings.json` on updates;
- not automatically configure the manager to launch at Windows login in V1 unless explicitly added later.

The user starts the manager when remote access is wanted. Starting the manager enables the service; exiting the manager disables it.

## Failure Handling

### Service missing

If `Remotely_Service` is not installed, show a red/error state and a clear message. Do not repeatedly retry as if the service were merely stopped.

### Start failure

Keep the manager open, show error state, expose the Windows error message where safe/useful, and allow retry.

### Stop failure on Exit

Do not silently exit. Explain that the service could not be confirmed stopped and offer retry/cancel exit.

### Settings parse failure

Fall back to Original behavior for the Desktop process and surface a warning in the manager. Do not make a corrupt settings file prevent remote control entirely.

### Settings file temporarily unavailable

Keep the last known-good in-memory settings and retry observation/read on subsequent file change or periodic refresh.

## Security and Privacy Considerations

- The manager must never persist session IDs or access keys.
- Server identity and device identity remain in upstream `ConnectionInfo.json`.
- `EmergencySettings.json` contains no authentication secret.
- The manager should not weaken Windows service permissions in V1.
- The server-side authentication and unattended authorization mechanisms remain authoritative.
- Exiting the tray must not merely hide the UI; it must stop and confirm the service state.

## Compatibility and Upstream Maintenance

Changes should be localized to reduce future fork maintenance cost:

- add manager-specific project/files rather than folding unrelated UI into Agent;
- keep settings types/providers small and separable;
- integrate stream settings through narrow Desktop.Shared seams;
- avoid changes to Server hubs, authentication, SignalR protocol, and session-cache behavior;
- avoid changing `ConnectionInfo.json` schema.

Because the currently deployed server image may not be pinned to the same source revision as the fork, this client customization should not depend on new server RPCs or server-side changes.

## Validation Criteria

### Service manager

- Launching Manager while service is stopped starts it successfully.
- Launching Manager while service is running leaves it running.
- Tray icon accurately reflects Running, Stopped, pending, and error states.
- External `Stop-Service Remotely_Service` is reflected in the tray without restarting Manager.
- Start/Stop menu actions control the actual service.
- Exit stops the service and only exits after Stopped is confirmed or the user acknowledges an error.
- Service remains Manual after install/update.

### Unattended remote control

- Starting Manager makes the device appear Online on the Remotely server.
- Remote Control from the web Devices page still uses the existing unattended flow.
- No Server URL prompt appears.
- No attended Accept prompt appears when server settings permit unattended access.
- Stopping the service makes the device go Offline.

### Quality settings

- Missing settings file yields original quality behavior.
- Original profile preserves upstream behavior.
- Balanced applies 65 / 15 FPS / audio Off.
- Emergency applies 45 / 8 FPS / audio Off.
- Ultra Low applies 30 / 5 FPS / audio Off.
- Custom validates and persists user values.
- Changing quality during an active session is visible without reconnecting within approximately 1 second.
- Active-session FPS changes take effect without reconnecting.
- Audio mode changes behave according to the approved live-change semantics.
- Lower profiles do not get automatically raised back to image quality 80 by auto-quality logic.

### Regression checks

- Mouse and keyboard input responsiveness remains independent of frame-rate limiting.
- Diff/changed-region capture remains enabled and unchanged.
- Server SignalR/Long Polling behavior is unchanged.
- Existing Resident Agent server connection and reconnect behavior remains unchanged.
- Desktop attended mode continues to work for users who explicitly run the portable Desktop client.

## Rollout Strategy

Implement and validate in layers:

1. Manager/service lifecycle only, with no stream changes.
2. Persisted settings model and UI.
3. Live settings observation in Desktop.
4. JPEG quality live adjustment.
5. FPS limiting.
6. Audio control.
7. Installer/package integration.
8. End-to-end test using the existing CloudBase deployment and internal-browser Long Polling path.

Each layer should remain independently testable and reversible.

## Open Implementation Questions

These are implementation investigations, not unresolved product requirements:

1. Which existing Desktop.Shared service is the cleanest owner for a machine-level runtime settings provider?
2. What is the narrowest safe point to apply an FPS gate without disturbing input/control traffic?
3. Can the existing audio capturer be safely stopped and restarted during a live session, or does audio require a small lifecycle wrapper?
4. Which Avalonia tray APIs/version-specific patterns best fit the repository's current Avalonia 11.1.4 dependency?
5. What packaging project/script should own the new Manager binary in the Windows zip and installer output?

If any investigation contradicts the approved runtime semantics, stop and return for design review rather than weakening behavior silently.
