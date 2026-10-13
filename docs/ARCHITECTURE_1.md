# ARCHITECTURE — The Hub

Scope: how the app is built today, how it must be built, and how we migrate **in place** (same repo, same solution, no rewrite). Read `PRD.md` for what each module does and `DESIGN_SYSTEM.md` for how it looks and moves.

## 1. Current state (facts from the code snapshot)

| Item | Fact |
|---|---|
| Solution | `TheHub.sln`: `TheHub.App` (WinExe `TheHub`), `TheHub.Core`, `TheHub.Common`, `TheHub.Overlays`, `TheHub.Tests` |
| Size | ~23k lines C#/XAML. `MainWindow.xaml.cs` ≈ 3,400 lines, `MainWindow.xaml` ≈ 2,350, `OverviewView.xaml` ≈ 850, `DynamicIslandWindow.xaml.cs` ≈ 1,150 |
| Tests | One file (`HubCoreTests.cs`, ~417 lines) |
| Process model | Single process. `App` creates overlay windows directly (`new RadialWindow()`, `new DynamicIslandWindow()` in `MainWindow.xaml.cs`) |
| Dead code | `TheHub.Overlays` is `WinExe` with its own `App` + `NamedPipeServer` (old multi-process design, never launched). `Common/IPC` is its transport |
| Windows | Island, Radial, Radial dimmer, Display dimmer, Live wallpaper (per monitor), Clock, CPU, GPU, RAM, Resource widgets |
| System access | `Core/Native` (Win32, COM audio), `Core/Hooks` (low-level mouse/keyboard), `Core/Monitors` (Volume, MediaSession, Clipboard, ActiveApp, Fullscreen) |
| Wallpaper engine | `FlyleafLib` + `Flyleaf.FFmpeg` (large native DLLs) |
| Settings | One flat `AppSettings` object (`settings.json`) with ~60 properties, including `AIAuditLogs` |
| Run path | Desktop shortcut → `TheHub-App\TheHub.exe`, a **copy** made by the `CopyToTheHubApp` MSBuild target after each build |
| Measured idle | Working Set ≈ 250MB, Private ≈ 176–194MB, ~28→19 threads, ~950–990 handles |

### Known risks (ranked)
1. **UI-thread blocking.** 17 `Dispatcher.Invoke` sites (10 in `MainWindow`, 3 in Island, 3 in dimmers, 1 in dead code). Monitor callbacks (COM `IAudioEndpointVolumeCallback`, media sessions, timers) can end up waiting on the UI thread while the UI thread calls back into COM.
2. **God window.** Everything is wired in `MainWindow.xaml.cs`, so edits collide and agents "change" the wrong place.
3. **Stale binary.** Running the copied `TheHub-App` exe hides failed builds. Fixed by build stamp + `dev-run.bat`.
4. **Settings god-object** and audit log inside it.
5. **Memory:** native video libraries are likely loaded even without an animated wallpaper (to be measured, task P0.6/P4.1).
6. **No design system**, so layout bugs (overlapping header buttons) repeat. See `DESIGN_SYSTEM.md`.

## 2. Target architecture (reached incrementally)

```
┌──────────────────────────── TheHub.App (shell) ────────────────────────────┐
│ MainWindow (sidebar + frame only)   Pages: Overview, Wallpapers, Island,   │
│ ViewModels per page (no logic in code-behind)  Radial, Workspaces, Widgets │
└───────────────▲──────────────────────────────────────────────▲─────────────┘
                │ commands / state                             │ views
┌───────────────┴──────────┐   ┌──────────────────────────────┴─────────────┐
│ TheHub.Core              │   │ TheHub.Overlays  (class library)           │
│  ModuleHost + IHubModule │◄──┤  IslandModule  RadialModule  WidgetsModule │
│  HubBus (typed events)   │   │  WallpaperModule  (each owns its windows)  │
│  UiBridge (UI marshaling)│   └──────────────────────────────▲─────────────┘
│  PowerPolicy             │                                  │ tokens + motion
│  Monitors / Hooks / Native│  ┌──────────────────────────────┴─────────────┐
│  Services                │   │ TheHub.Design (new WPF library)            │
└───────────────▲──────────┘   │  Tokens.xaml  Motion/  Controls/           │
                │              └────────────────────────────────────────────┘
┌───────────────┴──────────┐
│ TheHub.Common            │  Settings sections, models, logging
└──────────────────────────┘
```

`TheHub.Design` is a small library inside the same solution so both `App` and `Overlays` can share tokens, controls and the motion engine without circular references. It is **not** a rewrite.

## 3. Core contracts

### 3.1 Modules
```csharp
public interface IHubModule : IDisposable
{
    string Id { get; }                 // "wallpaper", "island", "radial", "workspaces", "widgets", "assistant"
    ModuleState State { get; }         // Stopped | Running | Suspended | Faulted
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    void Suspend(SuspendReason reason);  // Fullscreen | Battery | Workspace | User
    void Resume(SuspendReason reason);
}
```
`ModuleHost` registers modules, starts those enabled in settings, wraps each call in try/catch (a faulted module is restarted at most 3 times, then marked `Faulted` and logged; the app keeps running), and exposes per-module enable/disable to the UI.

### 3.2 Bus and UI marshaling
```csharp
public interface IHubBus
{
    void Publish<T>(T evt) where T : IHubEvent;       // never blocks, safe from any thread incl. COM callbacks
    IDisposable Subscribe<T>(Action<T> handler) where T : IHubEvent;
}
```
- Implementation: `System.Threading.Channels` (unbounded, single reader loop).
- `UiBridge` is the **only** place that touches `Dispatcher`: it batches events and calls `Dispatcher.InvokeAsync(..., DispatcherPriority.Normal)`; coalesces high-frequency events (volume, progress) to the latest value per frame.
- Events (initial set): `MediaChanged`, `VolumeChanged`, `MicMuteChanged`, `BrightnessChanged`, `UsbChanged`, `ClipboardChanged`, `DownloadProgress`, `PrayerTimeDue`, `PomodoroTick`, `WorkspaceChanged`, `FullscreenChanged(isFullscreen, hwnd)`, `ActiveAppChanged`, `PowerStateChanged`, `SettingsChanged(section)`.

### 3.3 PowerPolicy
Single service subscribing to `FullscreenChanged` and `PowerStateChanged` plus Workspace flags. It decides which modules to `Suspend`/`Resume` according to settings (`WallpaperPauseOnFullscreen`, `IslandHideOnFullscreen`, `FullscreenProtection`, `LowPowerMode`...). Modules never poll fullscreen themselves.

## 4. Threading rules (hard rules)

1. **No `Dispatcher.Invoke`, no `.Result`, no `.Wait()`, no `GetAwaiter().GetResult()`** on any thread. Use `InvokeAsync` (only inside `UiBridge`) and `await`.
2. **COM/native callbacks only enqueue**: they call `bus.Publish(...)` and return. No UI access, no further COM calls, no locks held while publishing.
3. **COM calls happen on a dedicated STA/worker context** (`AudioService` queue), never on the UI thread.
4. **Windows are created on the UI thread** by their module's `StartAsync` through `UiBridge`; never inside another module or `MainWindow`.
5. Long work (file scan, device enumeration, icon extraction) uses `Task.Run` and returns data, not UI objects.
6. Every `DispatcherUnhandledException` / `AppDomain.UnhandledException` / unobserved task exception is logged to `app_debug.log` with the module name.
7. A UI watchdog (task P1.6) logs when the UI thread is unresponsive for more than 1.5s, with the last 20 breadcrumbs.

## 5. Settings

Split `AppSettings` into sections, one JSON file each under `%AppData%\TheHub\` (or the current settings folder), loaded and saved independently:

| File | Contents |
|---|---|
| `app.json` | StartWithWindows, FullscreenProtection, PerformanceMode, fonts, UI scale |
| `wallpaper.json` | Wallpapers, ActiveWallpaperId, MonitorWallpapers, pause/mute/fit rules |
| `island.json` | all `Island*` settings |
| `radial.json` | all radial settings, `RadialProfiles` |
| `workspaces.json` | `Workspaces` |
| `widgets.json` | existing `WidgetsConfig` |
| `assistant.json` | AI settings, permissions, kill switch |
| `audit.jsonl` | AI audit log (append-only, rotated) |

Migration: on first launch, if `settings.json` exists, read it, write the new files, rename the old to `settings.json.migrated`. Reading must tolerate missing or extra fields. Writes are atomic (write temp, then replace) and debounced (500ms). A section change publishes `SettingsChanged(section)`.

## 6. Performance budgets and measurement

- Budgets live in `PRD.md` §5. They are enforced by `scratch/run_bench_10min.ps1` (exists) extended with an interaction phase: 50 volume changes, 20 media changes, island open/close x30, radial open x30, Workspace switch x10.
- Output: CSV of RAM (Working Set + Private), CPU, threads, handles every 5 seconds, plus a summary. Every phase ends with a before/after table in the status note.
- Handles must not grow more than ~5% over a 30-minute run; private bytes must plateau.

## 7. Windows overlays (technical guidance, validate by measurement)

- Transparent topmost windows (`AllowsTransparency=True`) must stay **small**. The Island host is fixed-size (largest state) and the visible capsule animates inside; pass clicks through outside the capsule (`WS_EX_TRANSPARENT` toggling or hit-test region) instead of resizing the window per frame.
- Animate only `RenderTransform` (translate/scale), `Opacity`, and the capsule's own size/corner radius. Never animate blur/shadow effects. Use `BitmapCache` for static complex subtrees. Stop animation clocks when hidden or suspended.
- Always-on-top windows must be hidden, not just transparent, while a module is suspended.

## 8. Migration strategy (strangler, in place)

1. Add new pieces beside the old (`Design`, `HubBus`, `ModuleHost`).
2. Move one module at a time behind `IHubModule`; the old wiring in `MainWindow.xaml.cs` is deleted in the same task that replaces it.
3. After each task: build, run tests, `dev-run.bat`, check the build stamp in the window title, run the benchmark, commit.
4. Order: foundations → Island → Radial → Wallpaper → Workspaces → Widgets → Assistant (details and acceptance in `IMPLEMENTATION_PLAN.md`).
5. File limit: no file over 400 lines after its module is migrated.

## 9. Dev tooling (keep)

- `dev-run.bat`: kill running instance → build → run; fails loudly on build error.
- Build stamp in log and window title.
- `scratch/run_bench_10min.ps1`: performance harness.
- **Showcase exporter** (`Showcase/ScreenSnapshotExporter`, `ViewSnapshotRegistry`): keep as a **developer-only** visual QA tool started with `--showcase` (hidden from the sidebar in normal runs). It renders real screens to PNG, so every UI task can attach before/after images. Fix its hard-coded counts ("17 screens").
