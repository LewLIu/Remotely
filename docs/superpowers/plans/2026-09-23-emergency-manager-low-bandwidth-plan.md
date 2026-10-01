# Emergency Manager and Low-Bandwidth Remote Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Windows tray manager that makes `Remotely_Service` an explicit on-demand remote-access switch and add live-adjustable quality/FPS/audio settings that preserve upstream behavior by default.

**Architecture:** Keep the existing server, SignalR/Long Polling, Resident Agent, authentication, and unattended-control flow unchanged. Put the persisted stream profile schema/store in `Shared`, expose a narrow runtime settings provider to `Desktop.Shared`, implement Windows file watching/audio/service control in Windows-specific projects, and ship a separate elevated Avalonia tray executable beside the existing Agent/Desktop payload.

**Tech Stack:** .NET 8, Avalonia 11.1.4, MSTest, Moq, `System.ServiceProcess.ServiceController`, existing SkiaSharp/NAudio stack, PowerShell packaging.

**Spec:** `docs/superpowers/specs/2026-09-23-emergency-manager-low-bandwidth-design.md`

## Global Constraints

- Windows service name remains exactly `Remotely_Service`.
- Persist emergency settings at `%ProgramData%\Remotely\EmergencySettings.json`; do not alter `ConnectionInfo.json`.
- Default / Original means JPEG quality 80 plus upstream FPS/audio behavior; do not invent a default FPS cap or force audio on/off.
- Presets are Balanced = 65 / 15 FPS / audio Off, Emergency = 45 / 8 FPS / audio Off, Ultra Low = 30 / 5 FPS / audio Off.
- Manual ranges are JPEG quality 20-90 and FPS 2-30.
- Quality/FPS/audio changes must affect an active Windows remote-control session without reconnecting, normally within 1 second.
- FPS limiting affects desktop-image production only; keyboard/mouse/control DTO handling must remain independent.
- Keep changed-region capture intact.
- Manager launch auto-starts the service if needed; Manager exit must stop and confirm the service before exiting.
- Installer sets `Remotely_Service` to `Manual`; V1 does not auto-start Manager at Windows login and does not weaken the service ACL.
- Do not change Server hubs, authentication, SignalR transport selection, Long Polling behavior, session IDs/access keys, or CloudBase deployment behavior.

## Review Focus

- Corrupt/missing/temporarily unreadable `EmergencySettings.json`: initial missing/corrupt state resolves to Original; a transient read failure after a good load keeps the last-known-good snapshot.
- Stop failure or timeout during Manager Exit: the UI remains open and clearly reports that remote access was not confirmed disabled.
- Service state changed outside Manager: tray state must converge to the real SCM state without restart.
- Rapid atomic settings rewrites: watcher debounce must publish one valid final snapshot and never expose partial JSON.
- Audio interaction: viewer-requested audio On must be suppressed by a local low-bandwidth profile and automatically restored when the local profile returns to Original, without losing the viewer's requested state.

## File Structure

Create or modify these focused units:

- `Shared/Enums/RemoteStreamProfile.cs` — Original/Balanced/Emergency/UltraLow/Custom profile identity.
- `Shared/Enums/RemoteAudioMode.cs` — `Original` or `Off`; Custom checkbox maps enabled => Original, disabled => Off.
- `Shared/Models/RemoteStreamSettings.cs` — immutable settings snapshot, bounds validation, preset factories.
- `Shared/Services/IEmergencySettingsStore.cs` — persistence contract.
- `Shared/Services/EmergencySettingsStore.cs` — JSON load/save and atomic replace.
- `Desktop.Shared/Abstractions/IRemoteStreamSettingsProvider.cs` — current snapshot + change event.
- `Desktop.Shared/Services/OriginalRemoteStreamSettingsProvider.cs` — cross-platform no-file fallback.
- `Desktop.Win/Services/EmergencySettingsProviderWin.cs` — `%ProgramData%` watcher/debounce/last-known-good provider.
- `Desktop.Shared/Services/FrameRateGate.cs` — per-session image-frame gate.
- `Desktop.Shared/Services/AudioPolicyController.cs` — combine viewer audio request with local policy.
- `Desktop.Shared/Services/Viewer.cs`, `ViewerFactory.cs`, `ScreenCaster.cs`, `DtoMessageHandler.cs` — narrow integration points only.
- `Manager.Win/*` — new elevated Avalonia tray/settings application.
- `Tests/Shared.Tests/*`, `Tests/Desktop.Win.Tests/*`, `Tests/Manager.Win.Tests/*` — automated tests.
- `Server/wwwroot/Content/Install-Remotely.ps1` — Manual service install, leave stopped after installation.
- `Utilities/Publish.ps1` — publish Manager into each Windows Agent zip under `Manager\`.
- `.azure-pipelines/Release Build.yml` — run the new/previously omitted Windows tests.
- `Remotely.sln` — include Manager and Manager tests.

---

### Task 1: Shared stream-settings schema, presets, validation, and atomic persistence

**Files:**
- Create: `Shared/Enums/RemoteStreamProfile.cs`
- Create: `Shared/Enums/RemoteAudioMode.cs`
- Create: `Shared/Models/RemoteStreamSettings.cs`
- Create: `Shared/Services/IEmergencySettingsStore.cs`
- Create: `Shared/Services/EmergencySettingsStore.cs`
- Create: `Tests/Shared.Tests/Services/EmergencySettingsStoreTests.cs`
- Create: `Tests/Shared.Tests/Models/RemoteStreamSettingsTests.cs`

**Interfaces:**
- Produces: `RemoteStreamSettings Original/Balanced/Emergency/UltraLow`, `bool TryValidate(out string error)`, `IEmergencySettingsStore.Load(out string? warning)`, `Task IEmergencySettingsStore.SaveAsync(RemoteStreamSettings, CancellationToken)`.
- Consumes: existing `System.Text.Json` reference in `Shared`.

- [ ] **Step 1: Write failing preset and validation tests**

```csharp
[TestMethod]
public void Presets_MatchApprovedValues()
{
    Assert.AreEqual(80, RemoteStreamSettings.Original.ImageQuality);
    Assert.IsNull(RemoteStreamSettings.Original.MaxFps);
    Assert.AreEqual(RemoteAudioMode.Original, RemoteStreamSettings.Original.AudioMode);

    Assert.AreEqual(new RemoteStreamSettings(1, RemoteStreamProfile.Balanced, 65, 15, RemoteAudioMode.Off), RemoteStreamSettings.Balanced);
    Assert.AreEqual(new RemoteStreamSettings(1, RemoteStreamProfile.Emergency, 45, 8, RemoteAudioMode.Off), RemoteStreamSettings.Emergency);
    Assert.AreEqual(new RemoteStreamSettings(1, RemoteStreamProfile.UltraLow, 30, 5, RemoteAudioMode.Off), RemoteStreamSettings.UltraLow);
}

[TestMethod]
public void Custom_RejectsOutOfRangeValues()
{
    Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 19, 8, RemoteAudioMode.Off).TryValidate(out _));
    Assert.IsFalse(new RemoteStreamSettings(1, RemoteStreamProfile.Custom, 45, 31, RemoteAudioMode.Off).TryValidate(out _));
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test Tests/Shared.Tests/Shared.Tests.csproj --filter "RemoteStreamSettingsTests|EmergencySettingsStoreTests"`

Expected: FAIL because the settings types/store do not exist.

- [ ] **Step 3: Implement immutable settings and exact preset semantics**

```csharp
public sealed record RemoteStreamSettings(
    int Version,
    RemoteStreamProfile Profile,
    int ImageQuality,
    int? MaxFps,
    RemoteAudioMode AudioMode)
{
    public static RemoteStreamSettings Original => new(1, RemoteStreamProfile.Original, 80, null, RemoteAudioMode.Original);
    public static RemoteStreamSettings Balanced => new(1, RemoteStreamProfile.Balanced, 65, 15, RemoteAudioMode.Off);
    public static RemoteStreamSettings Emergency => new(1, RemoteStreamProfile.Emergency, 45, 8, RemoteAudioMode.Off);
    public static RemoteStreamSettings UltraLow => new(1, RemoteStreamProfile.UltraLow, 30, 5, RemoteAudioMode.Off);

    public bool TryValidate(out string error)
    {
        if (Version != 1) { error = "Unsupported settings version."; return false; }
        if (ImageQuality is < 20 or > 90) { error = "Image quality must be between 20 and 90."; return false; }
        if (MaxFps is not null && MaxFps is < 2 or > 30) { error = "Max FPS must be between 2 and 30."; return false; }
        if (Profile == RemoteStreamProfile.Original && (ImageQuality != 80 || MaxFps is not null || AudioMode != RemoteAudioMode.Original))
        { error = "Original profile must preserve upstream behavior."; return false; }
        error = string.Empty;
        return true;
    }
}
```

Implement `EmergencySettingsStore` with a constructor-supplied path for tests and `CreateDefault()` resolving `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Remotely", "EmergencySettings.json")`. `Load` returns Original for missing/corrupt/invalid files and reports a warning. `SaveAsync` validates first, writes UTF-8 JSON to a same-directory `.tmp`, then uses `File.Replace` when the destination exists and `File.Move` when it does not; clean up a leftover temp file in `finally`.

- [ ] **Step 4: Add persistence edge-case tests**

Test exact cases: missing file => Original/no exception; corrupt JSON => Original/warning; unknown JSON fields tolerated; valid Custom round-trips; invalid values are refused; interrupted/temp file never replaces last valid destination.

- [ ] **Step 5: Run tests and commit**

Run: `dotnet test Tests/Shared.Tests/Shared.Tests.csproj --filter "RemoteStreamSettingsTests|EmergencySettingsStoreTests"`

Expected: PASS.

Commit:
```bash
git add Shared Tests/Shared.Tests
git commit -m "feat: add emergency stream settings model"
```

---

### Task 2: Live Windows settings provider with last-known-good semantics

**Files:**
- Create: `Desktop.Shared/Abstractions/IRemoteStreamSettingsProvider.cs`
- Create: `Desktop.Shared/Services/OriginalRemoteStreamSettingsProvider.cs`
- Modify: `Desktop.Shared/Startup/IServiceCollectionExtensions.cs`
- Create: `Desktop.Win/Services/EmergencySettingsProviderWin.cs`
- Modify: `Desktop.Win/Startup/IServiceCollectionExtensions.cs`
- Create: `Tests/Desktop.Win.Tests/EmergencySettingsProviderWinTests.cs`

**Interfaces:**
- Consumes: `IEmergencySettingsStore` and `RemoteStreamSettings` from Task 1.
- Produces: `IRemoteStreamSettingsProvider.Current` and `event EventHandler<RemoteStreamSettings>? SettingsChanged`.

- [ ] **Step 1: Write failing provider tests**

```csharp
[TestMethod]
public async Task FileChange_PublishesValidSnapshotWithinOneSecond()
{
    using var fixture = await ProviderFixture.CreateAsync(RemoteStreamSettings.Original);
    await fixture.Store.SaveAsync(RemoteStreamSettings.Emergency);
    await fixture.WaitForAsync(x => x.Current == RemoteStreamSettings.Emergency, TimeSpan.FromSeconds(1));
}

[TestMethod]
public async Task TransientUnreadableFile_KeepsLastKnownGood()
{
    using var fixture = await ProviderFixture.CreateAsync(RemoteStreamSettings.Balanced);
    fixture.MakeStoreReadFailOnce();
    fixture.RaiseFileChanged();
    await Task.Delay(300);
    Assert.AreEqual(RemoteStreamSettings.Balanced, fixture.Provider.Current);
}
```

Also pin rapid multiple watcher events and initial missing/corrupt file behavior.

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj --filter EmergencySettingsProviderWinTests`

Expected: FAIL because provider types do not exist.

- [ ] **Step 3: Implement provider contract and Windows watcher**

```csharp
public interface IRemoteStreamSettingsProvider
{
    RemoteStreamSettings Current { get; }
    event EventHandler<RemoteStreamSettings>? SettingsChanged;
}
```

`OriginalRemoteStreamSettingsProvider` always returns `RemoteStreamSettings.Original`. `EmergencySettingsProviderWin` loads once, watches the parent directory with `FileSystemWatcher` for Changed/Created/Renamed/Deleted, debounces 150 ms, validates via the shared store, atomically swaps the in-memory snapshot, and raises `SettingsChanged` only when the effective snapshot changes. A failed read after startup keeps the prior snapshot. A missing file at initial load uses Original. Register the Original provider in `AddRemoteControlXplat`; register the Windows provider after it in `AddRemoteControlWindows` so Windows direct resolution gets the Windows implementation.

- [ ] **Step 4: Run provider tests and full Windows test project**

Run:
```bash
dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj --filter EmergencySettingsProviderWinTests
dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj
```

Expected: PASS (manual `[Ignore]` tests remain skipped).

- [ ] **Step 5: Commit**

```bash
git add Desktop.Shared Desktop.Win Tests/Desktop.Win.Tests
git commit -m "feat: watch emergency settings at runtime"
```

---

### Task 3: Apply live JPEG quality without breaking Original auto-quality

**Files:**
- Modify: `Desktop.Shared/Services/Viewer.cs`
- Modify: `Desktop.Shared/Services/ViewerFactory.cs`
- Create: `Tests/Desktop.Win.Tests/ViewerQualityPolicyTests.cs`

**Interfaces:**
- Consumes: `IRemoteStreamSettingsProvider.Current`.
- Produces: existing `IViewer.ImageQuality` with Original behavior unchanged and configured profile quality applied immediately.

- [ ] **Step 1: Write failing quality-policy tests**

```csharp
[TestMethod]
public async Task Emergency_DoesNotAutoClimbBackTo80()
{
    var viewer = ViewerFixture.Create(RemoteStreamSettings.Emergency);
    for (var i = 0; i < 20; i++) await viewer.ApplyAutoQuality();
    Assert.AreEqual(45, viewer.ImageQuality);
}

[TestMethod]
public async Task SwitchingBackToOriginal_RestoresUpstreamAutoQuality()
{
    var fixture = ViewerFixture.CreateMutable(RemoteStreamSettings.Emergency);
    await fixture.Viewer.ApplyAutoQuality();
    fixture.Settings.Set(RemoteStreamSettings.Original);
    await fixture.Viewer.ApplyAutoQuality();
    Assert.AreEqual(47, fixture.Viewer.ImageQuality);
}
```

Also test Balanced=65, UltraLow=30, and Custom=90.

- [ ] **Step 2: Run RED test**

Run: `dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj --filter ViewerQualityPolicyTests`

Expected: FAIL because `Viewer` does not consume runtime settings.

- [ ] **Step 3: Inject settings provider and change only `ApplyAutoQuality` policy**

```csharp
public Task ApplyAutoQuality()
{
    var settings = _streamSettings.Current;
    if (settings.Profile != RemoteStreamProfile.Original)
    {
        ImageQuality = settings.ImageQuality;
        return Task.CompletedTask;
    }

    if (ImageQuality < DefaultQuality)
        ImageQuality = Math.Min(DefaultQuality, ImageQuality + 2);
    else if (ImageQuality > DefaultQuality)
        ImageQuality = DefaultQuality;

    return Task.CompletedTask;
}
```

Update `ViewerFactory` to resolve/pass `IRemoteStreamSettingsProvider`. Do not change JPEG encoding or diff-area calculation; `ScreenCaster` continues encoding cropped changes with `viewer.ImageQuality`.

- [ ] **Step 4: Run tests and commit**

Run: `dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj --filter ViewerQualityPolicyTests`

Expected: PASS.

Commit:
```bash
git add Desktop.Shared/Services/Viewer.cs Desktop.Shared/Services/ViewerFactory.cs Tests/Desktop.Win.Tests/ViewerQualityPolicyTests.cs
git commit -m "feat: apply live remote image quality"
```

---

### Task 4: Add per-session FPS gate without touching input/control DTOs

**Files:**
- Create: `Desktop.Shared/Services/FrameRateGate.cs`
- Modify: `Desktop.Shared/Startup/IServiceCollectionExtensions.cs`
- Modify: `Desktop.Shared/Services/ScreenCaster.cs`
- Create: `Tests/Desktop.Win.Tests/FrameRateGateTests.cs`

**Interfaces:**
- Consumes: nullable `RemoteStreamSettings.MaxFps`.
- Produces: `ValueTask WaitAsync(int? maxFps, CancellationToken cancellationToken = default)`; one transient gate per `ScreenCaster` session.

- [ ] **Step 1: Write deterministic limiter tests using `TimeProvider`**

```csharp
[TestMethod]
public async Task EightFps_EnforcesAbout125MsBetweenFrameAttempts()
{
    var fakeTime = new FakeTimeProvider();
    var gate = new FrameRateGate(fakeTime);
    await gate.WaitAsync(8);
    var second = gate.WaitAsync(8).AsTask();
    Assert.IsFalse(second.IsCompleted);
    fakeTime.Advance(TimeSpan.FromMilliseconds(125));
    await second;
}

[TestMethod]
public async Task OriginalNullCap_DoesNotDelay()
{
    var gate = new FrameRateGate(new FakeTimeProvider());
    await gate.WaitAsync(null);
    await gate.WaitAsync(null);
}
```

Use `Microsoft.Extensions.TimeProvider.Testing` only in the test project; production uses `TimeProvider.System`.

- [ ] **Step 2: Run RED tests**

Run: `dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj --filter FrameRateGateTests`

Expected: FAIL because gate does not exist.

- [ ] **Step 3: Implement gate and integrate immediately before frame capture**

```csharp
public async ValueTask WaitAsync(int? maxFps, CancellationToken cancellationToken = default)
{
    if (maxFps is null) { _nextFrameAt = null; return; }
    var interval = TimeSpan.FromSeconds(1d / maxFps.Value);
    var now = _timeProvider.GetUtcNow();
    if (_nextFrameAt is { } next && next > now)
        await Task.Delay(next - now, _timeProvider, cancellationToken);
    _nextFrameAt = _timeProvider.GetUtcNow() + interval;
}
```

In `ScreenCaster.GetDesktopStream`, after backpressure handling and before `Capturer.GetNextFrame()`, call `await _frameRateGate.WaitAsync(_streamSettings.Current.MaxFps)`. Move `viewer.IncrementFpsCount()` to the path where a non-empty encoded frame is actually emitted so session metrics report transmitted-frame FPS. Do not add any delay in `DtoMessageHandler` or SignalR control paths.

- [ ] **Step 4: Run focused and regression tests, then commit**

Run:
```bash
dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj --filter FrameRateGateTests
dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj
```

Expected: PASS.

Commit:
```bash
git add Desktop.Shared Tests/Desktop.Win.Tests
git commit -m "feat: cap remote desktop frame rate"
```

---

### Task 5: Enforce live local audio policy while preserving viewer intent

**Files:**
- Create: `Desktop.Shared/Services/AudioPolicyController.cs`
- Modify: `Desktop.Shared/Startup/IServiceCollectionExtensions.cs`
- Modify: `Desktop.Shared/Services/DtoMessageHandler.cs`
- Create: `Tests/Desktop.Win.Tests/AudioPolicyControllerTests.cs`

**Interfaces:**
- Consumes: `IAudioCapturer.ToggleAudio(bool)` and `IRemoteStreamSettingsProvider.SettingsChanged`.
- Produces: `IAudioPolicyController.SetViewerRequested(bool)`.

- [ ] **Step 1: Write failing policy tests**

```csharp
[TestMethod]
public void LowBandwidthProfile_SuppressesViewerAudioRequest_AndOriginalRestoresIt()
{
    var fixture = AudioPolicyFixture.Create(RemoteStreamSettings.Original);
    fixture.Policy.SetViewerRequested(true);
    fixture.Capturer.Verify(x => x.ToggleAudio(true), Times.Once);

    fixture.Settings.Set(RemoteStreamSettings.Emergency);
    fixture.Capturer.Verify(x => x.ToggleAudio(false), Times.Once);

    fixture.Settings.Set(RemoteStreamSettings.Original);
    fixture.Capturer.Verify(x => x.ToggleAudio(true), Times.Exactly(2));
}
```

Also test that repeated same effective state does not restart `WasapiLoopbackCapture` and that viewer-requested Off stays Off when returning to Original.

- [ ] **Step 2: Run RED test**

Run: `dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj --filter AudioPolicyControllerTests`

Expected: FAIL because policy controller does not exist.

- [ ] **Step 3: Implement controller and route ToggleAudio DTO through it**

```csharp
private void Apply()
{
    var shouldRun = _settings.Current.AudioMode != RemoteAudioMode.Off && _viewerRequested;
    if (shouldRun == _effectiveOn) return;
    _effectiveOn = shouldRun;
    _audioCapturer.ToggleAudio(shouldRun);
}
```

Subscribe to `SettingsChanged`, keep `_viewerRequested`, and dispose/unsubscribe with application lifetime. Replace direct `_audioCapturer.ToggleAudio(dto.ToggleOn)` in `DtoMessageHandler` with `_audioPolicy.SetViewerRequested(dto.ToggleOn)`. This is safe for live change because the existing Windows capturer already exposes start/stop through `ToggleAudio(bool)`.

- [ ] **Step 4: Run tests and commit**

Run: `dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj --filter "AudioPolicyControllerTests|ViewerQualityPolicyTests|FrameRateGateTests"`

Expected: PASS.

Commit:
```bash
git add Desktop.Shared Tests/Desktop.Win.Tests
git commit -m "feat: apply live local audio policy"
```

---

### Task 6: Build testable Manager service-lifecycle core

**Files:**
- Create: `Manager.Win/Manager.Win.csproj`
- Create: `Manager.Win/Services/RemotelyServiceState.cs`
- Create: `Manager.Win/Services/IRemotelyServiceController.cs`
- Create: `Manager.Win/Services/WindowsRemotelyServiceController.cs`
- Create: `Manager.Win/Services/ManagerCoordinator.cs`
- Create: `Tests/Manager.Win.Tests/Manager.Win.Tests.csproj`
- Create: `Tests/Manager.Win.Tests/ManagerCoordinatorTests.cs`
- Modify: `Remotely.sln` via `dotnet sln` commands.

**Interfaces:**
- Consumes: Windows SCM service `Remotely_Service`.
- Produces: actual-state polling, start/stop operations, `InitializeAsync()`, `RefreshAsync()`, `TryExitAsync(TimeSpan)`.

- [ ] **Step 1: Scaffold projects and add solution entries**

Run:
```bash
dotnet new classlib -n Manager.Win -o Manager.Win -f net8.0
dotnet new mstest -n Manager.Win.Tests -o Tests/Manager.Win.Tests -f net8.0
dotnet sln Remotely.sln add Manager.Win/Manager.Win.csproj Tests/Manager.Win.Tests/Manager.Win.Tests.csproj
```

Then change Manager target to `net8.0-windows`, `OutputType=WinExe`, and add package references `Avalonia`/`Avalonia.Desktop` 11.1.4 plus `System.ServiceProcess.ServiceController`; Manager references `Shared`. Manager tests reference Manager and use Moq/MSTest versions aligned with the existing test projects.

- [ ] **Step 2: Write failing coordinator tests**

Pin: startup starts Stopped; startup leaves Running untouched; missing service => Error; external state change is reflected by `RefreshAsync`; exit stops Running and returns true only after Stopped; stop timeout returns false and keeps Manager alive.

```csharp
[TestMethod]
public async Task Exit_StopTimeout_ReturnsFalse()
{
    var fake = new FakeServiceController(RemotelyServiceState.Running) { NeverCompletesStop = true };
    var coordinator = new ManagerCoordinator(fake);
    Assert.IsFalse(await coordinator.TryExitAsync(TimeSpan.FromMilliseconds(50)));
}
```

- [ ] **Step 3: Run RED tests**

Run: `dotnet test Tests/Manager.Win.Tests/Manager.Win.Tests.csproj`

Expected: FAIL because service-control classes are not implemented.

- [ ] **Step 4: Implement SCM adapter and coordinator**

`WindowsRemotelyServiceController` wraps `ServiceController("Remotely_Service")`, calls `Refresh()`, maps Running/Stopped/StartPending/StopPending, and uses bounded polling (250 ms) after Start/Stop. `ManagerCoordinator.InitializeAsync()` starts only from Stopped; `TryExitAsync()` requests stop and returns false on exception/timeout rather than pretending remote access is disabled.

- [ ] **Step 5: Run tests and commit**

Run: `dotnet test Tests/Manager.Win.Tests/Manager.Win.Tests.csproj`

Expected: PASS.

Commit:
```bash
git add Manager.Win Tests/Manager.Win.Tests Remotely.sln
git commit -m "feat: add on-demand service manager core"
```

---

### Task 7: Add Avalonia tray UI and graphical live quality settings

**Files:**
- Replace/modify: `Manager.Win/Manager.Win.csproj`
- Create: `Manager.Win/Program.cs`
- Create: `Manager.Win/App.axaml`
- Create: `Manager.Win/App.axaml.cs`
- Create: `Manager.Win/app.manifest`
- Create: `Manager.Win/ViewModels/SettingsViewModel.cs`
- Create: `Manager.Win/Views/SettingsWindow.axaml`
- Create: `Manager.Win/Views/SettingsWindow.axaml.cs`
- Create: `Manager.Win/Services/TrayIconStateProvider.cs`
- Create: `Manager.Win/Assets/Remotely_Icon.png` by copying the repository's existing `Assets/Remotely_Icon.png`.
- Create: `Tests/Manager.Win.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `ManagerCoordinator`, `IEmergencySettingsStore`, approved preset factories.
- Produces: tray state/menu, Settings window, Save/Apply atomic write, Exit stop confirmation.

- [ ] **Step 1: Write failing Settings ViewModel tests**

Pin exact preset transitions, Restore Defaults => Original, manual edit => Custom, quality/FPS bounds, and Save calling `IEmergencySettingsStore.SaveAsync` with the selected snapshot.

```csharp
[TestMethod]
public void EditingEmergencyQuality_ChangesProfileToCustom()
{
    var vm = SettingsViewModelFixture.Create();
    vm.SelectPreset(RemoteStreamProfile.Emergency);
    vm.ImageQuality = 55;
    Assert.AreEqual(RemoteStreamProfile.Custom, vm.Profile);
}
```

- [ ] **Step 2: Run RED tests**

Run: `dotnet test Tests/Manager.Win.Tests/Manager.Win.Tests.csproj --filter SettingsViewModelTests`

Expected: FAIL because UI model does not exist.

- [ ] **Step 3: Implement settings window and tray lifecycle**

Use Avalonia `TrayIcon` + `NativeMenu`. On App startup call `ManagerCoordinator.InitializeAsync`; refresh real SCM state every 1 second and immediately after actions. Menu items are exactly: status, Start Service, Stop Service, Settings..., Exit. Disable Start while Running/StartPending and Stop while Stopped/StopPending.

`SettingsWindow` exposes preset dropdown and numeric/slider controls. Original displays `Max FPS: Original behavior` and `Audio: Original behavior`; Custom uses quality 20-90, FPS 2-30, and an `Enable Audio` checkbox where checked maps to `RemoteAudioMode.Original` and unchecked maps to `Off`. Save writes the shared JSON; no direct Desktop IPC is added.

- [ ] **Step 4: Implement visual tray states and elevation**

`app.manifest` uses:
```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

`TrayIconStateProvider` derives Running/Stopped/Pending/Error icons at runtime from the embedded existing icon (normal color for Running, grayscale for Stopped, yellow status badge for pending, red status badge for error) using SkiaSharp and supplies a `WindowIcon`; keep this rendering isolated from service logic. Tooltip includes the actual state, e.g. `Remotely - Running`.

Exit handler:
```csharp
if (await _coordinator.TryExitAsync(TimeSpan.FromSeconds(10)))
    _desktopLifetime.Shutdown();
else
    await ShowErrorAsync("Remotely_Service could not be confirmed stopped. Remote access may still be enabled.");
```

OS shutdown/logoff invokes a shorter best-effort stop and does not block beyond the OS shutdown budget.

- [ ] **Step 5: Run automated tests plus manual tray smoke**

Run:
```bash
dotnet test Tests/Manager.Win.Tests/Manager.Win.Tests.csproj
dotnet build Manager.Win/Manager.Win.csproj -c Release
```

Manual smoke on Windows: service initially Stopped => launching Manager prompts UAC once and reaches Running/color icon; PowerShell `Stop-Service Remotely_Service` => tray turns gray within ~2 s; tray Start => Running; Settings Save persists; Exit => service confirmed Stopped before icon disappears.

- [ ] **Step 6: Commit**

```bash
git add Manager.Win Tests/Manager.Win.Tests
git commit -m "feat: add tray manager and quality settings UI"
```

---

### Task 8: Package Manager and change Resident Agent install to Manual/on-demand

**Files:**
- Modify: `Server/wwwroot/Content/Install-Remotely.ps1`
- Modify: `Utilities/Publish.ps1`
- Modify: `.azure-pipelines/Release Build.yml`
- Modify: `README.md` with a short fork-specific Windows manager usage section.

**Interfaces:**
- Consumes: built `Remotely_Manager.exe` and existing Windows Agent zip pipeline.
- Produces: Windows zip containing `Manager\Remotely_Manager.exe`, Manual/stopped service after install/update, CI coverage.

- [ ] **Step 1: Add a script-level regression check before changing installer behavior**

Create `Tests/Installer.Tests.ps1` that reads `Install-Remotely.ps1` and fails unless it contains `-StartupType Manual`, does not contain `Start-Service -Name Remotely_Service` inside `Install-Remotely`, and still preserves `ConnectionInfo.json` during update. Run it now and verify failure.

Run: `powershell -ExecutionPolicy Bypass -File Tests/Installer.Tests.ps1`

Expected: FAIL because upstream currently installs Automatic and starts the service.

- [ ] **Step 2: Change installer semantics narrowly**

Change:
```powershell
New-Service ... -StartupType Automatic ...
Start-Service -Name Remotely_Service
```

to:
```powershell
New-Service ... -StartupType Manual ...
# Deliberately leave Remotely_Service stopped; Remotely_Manager.exe is the user's enable switch.
```

Do not alter connection info generation, device identity, verification token logic, or uninstall behavior. `%ProgramData%\Remotely\EmergencySettings.json` is outside `$InstallPath`, so updates naturally preserve it.

- [ ] **Step 3: Publish Manager into an isolated subfolder in each Windows Agent zip**

In `Utilities/Publish.ps1`, after publishing Agent and before `Compress-Archive`, create `Agent\bin\publish\win-x64\Manager` and `win-x86\Manager`, then publish:

```powershell
dotnet publish /p:Version=$CurrentVersion /p:FileVersion=$CurrentVersion --runtime win-x64 --self-contained --configuration Release --output "$Root\Agent\bin\publish\win-x64\Manager" "$Root\Manager.Win"
dotnet publish /p:Version=$CurrentVersion /p:FileVersion=$CurrentVersion --runtime win-x86 --self-contained --configuration Release --output "$Root\Agent\bin\publish\win-x86\Manager" "$Root\Manager.Win"
```

Keep Manager dependencies under `Manager\` to avoid overwriting Agent/Desktop assemblies in the package root.

- [ ] **Step 4: Expand CI test coverage**

Change the pipeline test project glob to include:
```yaml
**\Server.Tests.csproj
**\Shared.Tests.csproj
**\Desktop.Win.Tests.csproj
**\Manager.Win.Tests.csproj
```

Add a PowerShell step for `Tests/Installer.Tests.ps1`. Do not alter Server publish/runtime settings.

- [ ] **Step 5: Run build/package checks**

Run:
```bash
dotnet test Tests/Shared.Tests/Shared.Tests.csproj
dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj
dotnet test Tests/Manager.Win.Tests/Manager.Win.Tests.csproj
powershell -ExecutionPolicy Bypass -File Tests/Installer.Tests.ps1
dotnet build Remotely.sln -c Release
```

On a Windows build machine with the existing publish prerequisites, run `powershell -ExecutionPolicy Bypass -File Utilities/Publish.ps1` and inspect `Server/wwwroot/Content/Remotely-Win-x64.zip`: it must contain `Remotely_Agent.exe`, `Desktop\Remotely_Desktop.exe`, and `Manager\Remotely_Manager.exe`.

Expected: all tests/builds PASS; package structure matches exactly.

- [ ] **Step 6: Commit**

```bash
git add Server/wwwroot/Content/Install-Remotely.ps1 Utilities/Publish.ps1 .azure-pipelines/Release\ Build.yml Tests/Installer.Tests.ps1 README.md
git commit -m "feat: package on-demand Remotely manager"
```

---

### Task 9: End-to-end acceptance on the proven CloudBase + enterprise Long Polling path

**Files:**
- Create: `docs/superpowers/validation/2026-09-23-emergency-manager-low-bandwidth-validation.md`
- No server protocol code changes.

**Interfaces:**
- Consumes: packaged fork build and current CloudBase Remotely deployment.
- Produces: evidence that service lifecycle and live quality changes work without regressing unattended access or enterprise Long Polling.

- [ ] **Step 1: Install the fork build on the personal Windows PC**

Use the generated Windows installer/package. Verify with PowerShell:
```powershell
Get-Service Remotely_Service | Select-Object Status, StartType
```

Expected immediately after install: `Status=Stopped`, `StartType=Manual`.

- [ ] **Step 2: Verify Manager/service lifecycle**

Launch `Manager\Remotely_Manager.exe`: one UAC prompt, service becomes Running, CloudBase web Devices shows PC Online. Stop externally with `Stop-Service Remotely_Service`: tray becomes gray and Devices goes Offline. Start from tray: Online again. Exit Manager: service confirms Stopped and device goes Offline.

- [ ] **Step 3: Verify unattended control regression**

With Manager running and `Enforce Attended Access` disabled, start Remote Control from Devices in enterprise Chrome. Expected: no Server URL prompt, no attended Accept prompt, browser still uses repeated `/hubs/viewer` Long Polling GETs plus POSTs, and keyboard/mouse remain responsive.

- [ ] **Step 4: Verify all live profiles during one uninterrupted session**

Without reconnecting: Original -> Balanced -> Emergency -> Ultra Low -> Custom -> Original. Record timestamp, selected profile, Desktop session metric FPS, qualitative text readability, and CloudBase/browser transferred-byte sample. Expected: quality/FPS visibly change within ~1 s; low profiles never drift back to Q80; returning Original restores original quality/timing semantics.

- [ ] **Step 5: Verify live audio policy**

Turn browser audio on under Original; switch to Emergency and confirm capture/sending stops; switch back to Original and confirm the prior viewer request resumes audio without reconnecting. Then turn browser audio off and confirm later profile toggles do not force it back on.

- [ ] **Step 6: Verify failure cases**

Temporarily corrupt the settings JSON while connected: Desktop stays connected and uses last-known-good/Original safe behavior as applicable; Manager surfaces warning. Simulate stop timeout/failure in a test build/fake adapter: Exit must not close the tray while the service is unconfirmed. Confirm no session ID/access key is written to `EmergencySettings.json`.

- [ ] **Step 7: Record results and run the final automated suite**

Document each criterion as PASS/FAIL plus observed values in the validation markdown.

Run:
```bash
dotnet test Tests/Shared.Tests/Shared.Tests.csproj
dotnet test Tests/Desktop.Win.Tests/Desktop.Win.Tests.csproj
dotnet test Tests/Manager.Win.Tests/Manager.Win.Tests.csproj
powershell -ExecutionPolicy Bypass -File Tests/Installer.Tests.ps1
dotnet build Remotely.sln -c Release
```

Expected: all PASS; no changes required to CloudBase Server, ViewerHub, SignalR, or authentication.

- [ ] **Step 8: Commit validation evidence**

```bash
git add docs/superpowers/validation/2026-09-23-emergency-manager-low-bandwidth-validation.md
git commit -m "test: validate emergency manager end to end"
```

## Self-Review Results

- **Spec coverage:** Manager lifecycle, Manual service install, presets/Custom UI, atomic persistence, live quality/FPS/audio, failure handling, packaging, and CloudBase/Long Polling regression are each owned by a task.
- **Placeholder scan:** No TBD/TODO/deferred implementation steps remain; live audio feasibility is resolved by the existing `IAudioCapturer.ToggleAudio(bool)` / `AudioCapturerWin.Start/Stop` seam.
- **Type consistency:** Tasks consistently use `RemoteStreamSettings`, `RemoteStreamProfile`, `RemoteAudioMode`, `IEmergencySettingsStore`, `IRemoteStreamSettingsProvider`, `IFrameRateGate`, and `IAudioPolicyController` with the signatures defined above.
- **Review Focus coverage:** corrupt/transient settings are pinned in Tasks 1-2; exit-stop failure and external SCM changes in Task 6; rapid watcher writes in Task 2; viewer/local audio state restoration in Task 5.
- **Scope control:** no server RPC/protocol/authentication change, no display-resolution switching, no service ACL weakening, no login auto-start.
