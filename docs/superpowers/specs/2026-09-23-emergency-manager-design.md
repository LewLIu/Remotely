# Remotely Emergency Manager Design

Date: 2026-09-23
Repository: `LewLIu/Remotely`
Branch: `feature/emergency-manager`

## 1. Purpose

Add a Windows-focused emergency-use control layer on top of Remotely so the Resident Agent is not required to run continuously, while also allowing live control of remote-session bandwidth and image quality.

The intended use case is occasional emergency remote access from a restricted browser-only environment. The existing Remotely server, SignalR transport, browser viewer, Resident Agent registration, and unattended-session security flow are already working and must remain unchanged.

Success means:

- Opening the tray manager starts `Remotely_Service` when needed.
- Closing the tray manager stops `Remotely_Service` before the tray process exits.
- The tray icon reflects the real Windows Service state, including changes made outside the tray app.
- The Windows Service remains demand-start/manual rather than permanently running.
- Remote quality can be changed from a graphical settings window.
- Quality/FPS/audio changes apply to an active remote-control session without reconnecting.
- Default behavior remains equivalent to upstream Remotely; bandwidth-saving behavior is opt-in.

## 2. Non-goals

This work must not change:

- Remotely Server protocol or authentication.
- SignalR hub contracts.
- Browser viewer transport selection or Long Polling behavior.
- WebRTC/STUN/TURN behavior; Remotely does not depend on them in this deployment.
- Resident Agent identity, organization registration, access keys, session IDs, or server verification.
- CloudBase deployment architecture.
- Desktop resolution by default.

The first version will not add automatic display-resolution switching, adaptive network measurement, cloud-side quality policies, or a mobile client.

## 3. Existing behavior to preserve

The Resident Agent is installed as Windows service `Remotely_Service` and launches the unattended Desktop process with server/session/access-key parameters when a remote-control request arrives.

The Desktop screen caster already:

- sends JPEG-encoded screen content,
- uses `ImageQuality`, whose upstream default is 80,
- detects changed screen regions and skips frames when nothing changed,
- performs upstream flow-control/latency handling.

The new design must preserve all of those behaviors when the `Default / Original` preset is selected.

## 4. High-level architecture

Add a Windows tray application, tentatively named `Remotely_Manager.exe`.

```text
Remotely_Manager.exe
        |
        +-- Windows Service control --> Remotely_Service
        |
        +-- writes shared settings --> %ProgramData%\Remotely\EmergencySettings.json
                                         |
                                         v
Resident Agent --> Remotely_Desktop.exe --> live settings provider
                                         |
                                         +-- JPEG quality override
                                         +-- FPS cap override
                                         +-- audio override
```

The manager and Desktop process communicate only through the shared settings file. No new network protocol or server endpoint is introduced.

## 5. Tray manager behavior

### 5.1 Startup

When `Remotely_Manager.exe` starts:

1. Request administrator elevation once for the lifetime of the tray process.
2. Verify `Remotely_Service` exists.
3. Ensure its startup type is Manual/Demand Start.
4. Read the current real service state.
5. If the service is stopped, start it automatically.
6. Wait for a terminal state and update the tray icon.

The manager must never assume that its last command succeeded. UI state must be driven by the actual Windows Service status.

### 5.2 Runtime service state

The tray app periodically refreshes `Remotely_Service` state so external changes are reflected automatically.

Suggested icon states:

- Color: Running
- Gray: Stopped
- Yellow: StartPending / StopPending / transitional
- Red: Service missing or service-control error

The menu should include:

```text
Remotely
----------------------
Service: Running/Stopped
Start Service
Stop Service
Settings...
----------------------
Exit
```

Start/Stop actions should be disabled when they do not make sense for the current state.

### 5.3 Exit semantics

`Exit` means "disable remote access and exit manager":

1. Stop `Remotely_Service` if it is running.
2. Wait for `Stopped` up to a bounded timeout.
3. Exit the tray process only after stop succeeds.
4. If stop fails, show an error and keep the tray manager alive rather than silently leaving the service running.

During Windows logoff/shutdown, perform best-effort service stop. Because the service startup type is Manual, it must not automatically restart at the next boot.

### 5.4 Single-instance behavior

Only one manager instance should run per interactive user session. Starting a second instance should activate/open the existing instance rather than create competing service controllers.

## 6. Quality settings UX

The settings window exposes presets plus custom controls.

### 6.1 Presets

| Preset | JPEG Quality | Max FPS | Audio | Meaning |
|---|---:|---:|---|---|
| Default / Original | 80 | Original behavior | Original behavior | Preserve upstream experience |
| Balanced | 65 | 15 | Off | Light bandwidth reduction |
| Emergency | 45 | 8 | Off | Default low-bandwidth emergency profile |
| Ultra Low | 30 | 5 | Off | Maximum bandwidth saving |
| Custom | user-defined | user-defined | user-defined | Manual override |

`Default / Original` is the initial state when no settings file exists and is also the target of `Restore Defaults`.

Selecting a preset updates the controls immediately. Manually changing a preset-controlled field moves the mode to `Custom`.

### 6.2 Control ranges

Recommended validation ranges:

- JPEG quality: 20-90
- Max FPS: 2-30 when explicitly overridden
- Audio: On/Off when explicitly overridden

Values outside supported ranges are rejected rather than silently clamped on save.

### 6.3 Original behavior representation

The file schema must distinguish "explicit override" from "use upstream behavior".

For example:

```json
{
  "schemaVersion": 1,
  "preset": "Original",
  "imageQuality": 80,
  "maxFps": null,
  "audioEnabled": null
}
```

For an Emergency preset:

```json
{
  "schemaVersion": 1,
  "preset": "Emergency",
  "imageQuality": 45,
  "maxFps": 8,
  "audioEnabled": false
}
```

A null `maxFps` or `audioEnabled` means "defer to upstream behavior" rather than imposing a new default.

## 7. Shared settings storage

Settings path:

```text
%ProgramData%\Remotely\EmergencySettings.json
```

Requirements:

- No credentials, access keys, server verification tokens, organization IDs, or device IDs are stored in this file.
- Writes are atomic: write to a temporary file, flush, then replace/move into place.
- The file is readable by the Desktop process regardless of whether it is running in an interactive SYSTEM context or normal interactive-user context.
- Write access should be limited to Administrators/SYSTEM when practical.
- Missing, malformed, or unsupported files must safely fall back to `Default / Original`.
- Include `schemaVersion` for future migration.

The existing `ConnectionInfo.json` remains the single source of truth for Remotely server/device identity and is not duplicated.

## 8. Live settings application

### 8.1 Settings provider

Add a small shared settings model and a Desktop-side provider.

The Desktop-side provider should:

- load the current settings at startup,
- monitor the file for changes,
- debounce partial/multiple filesystem events,
- retry briefly after a change if a concurrent write is observed,
- update an immutable/current settings snapshot atomically,
- expose change notification or a cheap current-value getter.

A `FileSystemWatcher` may be used, but a lightweight periodic timestamp check is acceptable as a fallback for reliability. Target user-visible application latency is under 1 second.

### 8.2 JPEG quality

Do not replace upstream `Viewer.ApplyAutoQuality()` behavior globally.

Instead, when encoding a frame:

- if a quality override is active, encode with the configured value,
- otherwise use the current upstream `viewer.ImageQuality` value.

This avoids breaking upstream adaptive behavior and ensures `Default / Original` remains faithful to Remotely.

Changing quality during a session applies to the next encoded frame.

### 8.3 FPS cap

When `maxFps` is null, preserve current upstream timing and flow-control behavior.

When `maxFps` is set, add a send/capture pacing guard so no more than the configured number of screen-update frames are transmitted per second.

The cap must coexist with existing change-region detection and backpressure. It must not force frames when the desktop is static.

A live setting change must take effect without reconnecting.

### 8.4 Audio

When `audioEnabled` is null, preserve upstream audio behavior.

When false, prevent audio samples from being sent to the viewer while leaving the rest of the remote-control session untouched.

When switched from false to true during an active session, audio delivery should resume without reconnecting if the existing audio capture pipeline is already active. If the current architecture cannot safely resume without recreating the capturer, the implementation plan must explicitly document the limitation before coding it.

## 9. Project structure

Preferred structure:

```text
Manager.Win/
  Manager.Win.csproj
  Program.cs / App.axaml
  Services/
    RemotelyServiceController.cs
    SingleInstanceService.cs
  ViewModels/
    SettingsViewModel.cs
  Views/
    SettingsWindow.axaml
  Assets/
    tray-running.*
    tray-stopped.*
    tray-pending.*
    tray-error.*

Shared/
  Models/
    EmergencySettings.cs
  Services/
    EmergencySettingsStore.cs

Desktop.Shared/
  Services/
    EmergencySettingsProvider.cs
```

Exact names may change during implementation if they better match existing repository conventions, but the dependency direction should remain:

```text
Manager.Win --> Shared
Desktop.Shared --> Shared
```

The Manager should not depend on Server and should avoid pulling in Desktop rendering/capture dependencies.

## 10. Installer and deployment behavior

The custom Windows distribution should install:

- existing Remotely Agent/Desktop files,
- `Remotely_Manager.exe`,
- the service as Manual/Demand Start.

For compatibility with an existing upstream installation, the manager should also idempotently verify/normalize `Remotely_Service` to Manual when it starts.

This lets an existing machine adopt the manager without first reinstalling the service package.

The current server-side unattended access flow remains unchanged. The manager is local-only and is not required for server startup.

## 11. Error handling

The tray app should provide clear local errors for:

- service missing,
- start timeout,
- stop timeout,
- access denied/elevation failure,
- settings read/write failure,
- malformed settings.

Failures must not be represented as a healthy/color tray icon.

The Desktop-side settings provider should log invalid configuration and continue using `Default / Original` rather than terminating a remote session.

## 12. Security boundaries

The manager is an administrative local utility. It must:

- control only the fixed service name `Remotely_Service`,
- avoid generic arbitrary-service or command execution features,
- avoid passing settings through shell strings where a direct API is available,
- validate all numeric settings,
- keep all remote authentication/session logic in existing Remotely components,
- never store access keys or authentication data in `EmergencySettings.json`.

## 13. Testing strategy

### Automated tests

Add unit tests for:

- preset-to-settings resolution,
- Original/null override semantics,
- settings validation ranges,
- malformed/missing settings fallback,
- atomic settings load/update behavior,
- service-state-to-tray-state mapping using an abstracted/fake service controller,
- quality override resolution versus upstream `viewer.ImageQuality`,
- FPS-cap timing logic,
- audio override logic,
- live settings reload behavior.

### Windows manual/integration verification

Verify on a Windows machine with an installed Resident Agent:

1. Set service startup type to Manual.
2. Start Manager while service is stopped; service becomes Running and icon becomes color.
3. Stop service externally via PowerShell; tray turns gray.
4. Start service from tray; device returns Online in Remotely.
5. Exit manager; service reaches Stopped and manager exits.
6. Reboot; service remains stopped until Manager is launched.
7. Start an unattended remote session with Original preset; confirm existing Remotely quality/behavior is unchanged.
8. While connected, switch Original -> Balanced -> Emergency -> Ultra Low and confirm visible quality/FPS change without reconnecting.
9. Move sliders to Custom and confirm the current session updates within 1 second.
10. Toggle audio off/on and verify the implemented live behavior.
11. Confirm server, SignalR, viewer, and browser transport behavior are unchanged.

## 14. Rollback

All work is isolated on the fork branch `feature/emergency-manager`.

Rollback options:

- Stop using/uninstall the manager and restore service startup type to Automatic if desired.
- Delete `%ProgramData%\Remotely\EmergencySettings.json`; Desktop must fall back to upstream behavior.
- Run the original upstream Agent/Desktop binaries; no server migration is required.

No server database or protocol migration is introduced, so rollback does not require CloudBase changes.

## 15. Acceptance criteria

The feature is complete when all of the following are true:

- `Remotely_Service` can remain Manual and stopped when remote access is not wanted.
- Launching Manager starts the service automatically.
- Tray icon/menu reflects real service state.
- Tray Start and Stop work reliably.
- Exit stops the service before manager termination.
- Default/Original preset preserves the current upstream Remotely experience, including JPEG quality 80 and native FPS/audio behavior.
- Balanced, Emergency, and Ultra Low presets are available with the agreed values.
- Custom quality/FPS/audio settings are available through GUI controls.
- Changes take effect in the current remote session without reconnecting, subject only to any documented audio-pipeline limitation discovered during implementation.
- No changes are required to the existing CloudBase/SignalR/Long Polling server path.
- Existing unattended access continues to work normally.
