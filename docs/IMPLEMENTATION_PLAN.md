# IMPLEMENTATION PLAN — The Hub (in-place)

Same repo, same solution. Each task is small, testable, and ends with evidence. Give the agent **one task at a time** (or one phase if it proves reliable). Rules are in `AGENTS.md`.

**Task prompt template**
```
Do task <ID> from IMPLEMENTATION_PLAN.md. Read PRD.md, ARCHITECTURE.md, DESIGN_SYSTEM.md, AGENTS.md first.
Stay inside the files listed. Follow the Definition of Done in AGENTS.md. Report in Arabic.
```

Conventions: paths are relative to the repo root. "Accept" lines are the checks the agent must show evidence for. Facts about current code (line numbers, sizes) come from the snapshot analyzed earlier and must be re-verified by the agent.

---

## Phase 0 — Safety net and baseline (no behavior change)

**P0.1 Stable baseline.** Commit everything on `main`/branch `stable-baseline`, tag `v0-baseline`. Verify `.gitignore` contains `bin/ obj/ .vs/ .venv/ node_modules/ Screenshots/ TheHub-App/`. Decide whether `TheHub.App/FFmpeg/` is produced by package restore (then ignore it) or required (then document the download step; do not commit the binaries). *Accept:* clean `git status`; repository size reasonable; fresh clone builds.

**P0.2 Reliable run path.** Create `dev-run.bat` (taskkill `TheHub.exe` → `dotnet build` → stop on failure → `dotnet run --project TheHub.App --no-build`). Point the desktop shortcut to it. Keep the build stamp (log + window title). *Accept:* change a visible label, run, label changes; a deliberate compile error prevents launch.

**P0.3 Remove the dead second host.** `TheHub.Overlays`: change `OutputType` to `Library`; delete `App.xaml`, `App.xaml.cs`, the named-pipe server and their usages; remove `TheHub.Common/IPC/*` if nothing else uses it (verify by search). *Accept:* build 0 errors; app behaves the same; no `TheHub.Overlays.exe` in outputs.

**P0.4 Showcase becomes a dev tool.** Remove "Showcase Gallery" from the sidebar. Start it only with `--showcase`. Replace hard-coded counts with computed values. Files: `TheHub.App/MainWindow.xaml(.cs)`, `TheHub.App/Showcase/*`, `TheHub.App/App.xaml.cs`. *Accept:* normal run has no gallery; `--showcase` opens it and exports PNGs.

**P0.5 Settings golden file.** Copy the owner's real `settings.json` to `TheHub.Tests/Fixtures/settings.golden.json` (strip personal paths). Add tests that load and re-save it without loss. *Accept:* tests pass.

**P0.6 Baseline measurements.** (a) Create a minimal empty WPF app **outside the solution** (`scratch/EmptyWpf`) and measure Working Set/Private at idle. (b) Run The Hub with each module disabled in turn (wallpaper, island, radial, widgets) and record RAM/CPU/threads/handles. Write `docs/perf-baseline.md` with the table and update the budgets in `PRD.md` §5. *Accept:* table exists; each module's marginal cost is known.

**Gate P0:** tag `v0-baseline` exists, run path verified, baseline table written.

---

## Phase 1 — Foundations (design, motion, bus, modules, settings)

**P1.1 `TheHub.Design` project.** New WPF class library in the solution, referenced by `TheHub.App` and `TheHub.Overlays`. Add `Tokens.xaml` with all tokens from `DESIGN_SYSTEM.md` §2–4 (colors, brushes, type styles, spacing, radii). Map the existing `TheHub.App/ThemeDictionary.xaml` keys to tokens; no visual change yet. *Accept:* build; a test page uses tokens only.

**P1.2 Motion engine.** `TheHub.Design/Motion/SpringAnimator.cs`, `MotionTokens.cs` (§6.2), extension methods (`SpringTo`, `FadeTo`), one shared `CompositionTarget.Rendering` tick that stops when all animations rest. Unit tests: settles within tolerance; retargeting preserves velocity; critically-damped has no overshoot; stops ticking at rest (0 callbacks). A developer demo page in Showcase. *Accept:* tests pass; demo runs 60fps (reported CPU %).

**P1.3 `HubBus`.** `TheHub.Core/Bus/IHubBus.cs`, `HubBus.cs` (Channels, single reader loop), event records listed in `ARCHITECTURE.md` §3.2. Tests: publish from 8 threads, ordering per publisher, subscriber exception does not break others, dispose unsubscribes. *Accept:* tests pass.

**P1.4 `UiBridge`.** Single class using `Dispatcher.InvokeAsync`; batching and per-key coalescing (latest value wins for volume/progress). *Accept:* tests with a fake dispatcher; a grep shows `Dispatcher` is used only here and in `*.xaml.cs` of windows for window-local work.

**P1.5 `ModuleHost` and `PowerPolicy`.** `IHubModule`, `ModuleHost` (restart up to 3 times, then `Faulted`), `PowerPolicy` consuming `FullscreenChanged`/`PowerStateChanged` and settings. Move fullscreen detection output to the bus (`FullscreenDetector` publishes). *Accept:* tests with fake modules; simulated fullscreen suspends only modules configured to pause.

**P1.6 Diagnostics.** Global exception logging with module name; UI watchdog (ping UI thread each second; if no response for 1.5s, log last 20 breadcrumbs and all thread stacks summary). `Breadcrumbs.Add("island:expand")` helper. *Accept:* forced `Thread.Sleep` on the UI thread in a debug flag produces a watchdog log.

**P1.7 Settings split.** Implement section files and migration per `ARCHITECTURE.md` §5; move `AIAuditLogs` to `audit.jsonl`; debounced atomic writes; `SettingsChanged(section)`. Keep `SettingsManager` API compatible through an adapter so old call sites compile; convert call sites module by module in later phases. *Accept:* golden file migrates and round-trips; old file kept as `.migrated`; corrupted section file falls back to defaults with a log line, other sections unaffected.

**P1.8 Core controls.** `HubButton`, `HubToggle`, `HubSlider`, `HubChip`, `HubCard`, `SettingRow`, `SectionHeader`, `PreviewTile` with states and motion (§5, §6.3). A "Controls" page in Showcase. *Accept:* screenshots at widths 900/1100/1400 and scale 1.0/1.25; §9 checklist ticked.

**P1.9 Shell.** New `MainWindow` shell: sidebar + content frame; pages are `UserControl` + ViewModel; page transition per §6.3. Move the existing pages into the shell without rewriting their internals. **No new logic in `MainWindow.xaml.cs`;** start deleting moved code. Fix the overlapping header pattern using `SectionHeader`. *Accept:* every existing page still opens; no overlap at any tested width; build number visible in the title.

**Gate P1:** tests pass; benchmark not worse than baseline; zero new `Dispatcher.Invoke`.

---

## Phase 2 — Dynamic Island

**P2.1 `IslandModule`.** Implements `IHubModule`; creates `DynamicIslandWindow` through `UiBridge`. Remove island creation and wiring from `MainWindow.xaml.cs` (around lines 91–92, 267, 425 in the snapshot). *Accept:* island works as before; `MainWindow.xaml.cs` no longer references `DynamicIslandWindow`.

**P2.2 Island state machine (pure C#).** `TheHub.Overlays/DynamicIsland/IslandStateMachine.cs`: states from `PRD.md` §3.2, priority rules, coalescing, persistent slots, timers (`IslandDurationMs`, hover pause). No WPF types. Tests: higher priority preempts; same type updates in place; persistent music returns after a transient event; dismiss timing. *Accept:* ≥ 15 tests pass.

**P2.3 Monitors → bus.** `VolumeMonitor`, `MediaSessionMonitor`, `ClipboardMonitor`, `ActiveAppMonitor` publish events instead of raising UI-bound events. COM callbacks only enqueue. Audio COM calls go through a single worker queue in `AudioService`. Files: `TheHub.Core/Monitors/*`, `Services/AudioService.cs`, `Native/SystemControlHelper.cs`, `CoreHubManager.cs`. *Accept:* script that changes volume 200 times in 20 seconds (keys or API) causes no UI stall (watchdog log empty).

**P2.4 Remove remaining blocking calls in Island code.** All `Dispatcher.Invoke` in `DynamicIslandWindow.xaml.cs` and `DisplayDimmerOverlay.cs` replaced; the device enumeration at ~line 432 uses `Task.Run` + bus/UiBridge. *Accept:* grep gate passes for these files.

**P2.5 Island visual rebuild: compact states.** Fixed-size host window, capsule with spring morph (§6.4). States: Idle, Music (waves), Volume, Mic Muted. Click-through outside the capsule. *Accept:* Showcase screenshots of each; 60fps check; drag and `IslandLockPosition` work.

**P2.6 Expanded states.** Media Player (cover, title, seek, controls), Live Controls (volume/brightness/mic sliders), Workspaces drawer (reads `WorkspaceConfig` list; activation wired in P5). Transitions per §6.4. *Accept:* 50 rapid open/close cycles without flicker or stuck state.

**P2.7 Event states and missing services.** Verify first which of these exist in `TheHub.Core`: USB watcher, download tracker, prayer times, Pomodoro. Implement the missing ones as services publishing bus events: `UsbWatcher`, `DownloadWatcher` (watch the user's Downloads folder for growing partial files), `PrayerTimeService` (offline calculation; settings: city/coordinates, method defaulting to Umm al-Qura, adhan alert lead time), `PomodoroService` (focus/break lengths). Island cards for each. A new package for prayer calculation is allowed with a `DECISIONS.md` note. *Accept:* each event shows with correct content and returns to the persistent state.

**P2.8 Island settings page.** Rebuild "Island Studio" with `SettingRow`s for every property in `PRD.md` §3.2; live preview of position/scale. *Accept:* every setting takes effect without restart.

**Gate P2:** interaction stress (50 volume changes, 20 media changes, 30 expand/collapse, USB event) → 0 watchdog entries; benchmark within budget.

---

## Phase 3 — Radial Launcher

**P3.1 `RadialModule` and gesture recognizer.** `RadialGestureRecognizer` (pure, tested): middle-button hold (`HoldDelayMs`), flick direction, `ActivationZone`, cancel conditions, `Alt+Z`/`Alt+Space` triggers. Hooks publish raw input to the bus; the recognizer runs off the UI thread. *Accept:* ≥ 12 tests; no logic in the low-level hook callbacks beyond enqueue.

**P3.2 Wheel visuals and motion.** `RadialWindow`, `RadialWheelCanvas`: open/target/confirm/cancel per §6.5; settings for radius, icon size, spacing, opacity, dimming, labels, hover color. *Accept:* first frame ≤ 150ms after trigger (timestamps in log); no animation on hidden window.

**P3.3 Radial settings pages.** Triggers, Appearance, Slices editor, profile switcher, icon picker (180+ glyphs), installed-apps picker. *Accept:* all `PRD.md` §3.3 properties editable; changes apply live.

**P3.4 Fullscreen and dimmers.** `FullscreenProtection` handled by `PowerPolicy`; `RadialDimmerOverlay` blocking calls removed. *Accept:* wheel does not appear over a fullscreen game when protection is on.

**Gate P3:** 30 open/confirm cycles show stable handles/RAM; benchmark within budget.

---

## Phase 4 — Wallpapers

**P4.1 Lazy video engine.** Isolate FlyleafLib/FFmpeg in `WallpaperEngine` loaded only when an animated wallpaper is activated; unload when none. *Accept:* measured RAM with no animated wallpaper is lower than P0.6 and shows no Flyleaf modules loaded (report the module list).

**P4.2 `WallpaperModule` and pause rules.** Suspend/resume via `PowerPolicy` (fullscreen, battery, workspace). Suspend must stop decoding (CPU ≈ 0%), not only hide. *Accept:* entering fullscreen stops decode within 1s (log timestamps); CPU ≈ 0%.

**P4.3 Monitors and fit.** `MonitorWallpapers`, `SelectedTargetMonitor`, `WallpaperFitMode`, cross-fade switching (§6.6). *Accept:* two-monitor test (or simulated) and no flash on switch.

**P4.4 Wallpapers page.** Grid of `PreviewTile`s, import (drag/drop), apply per monitor, state badges, error states for missing files. *Accept:* §9 checklist.

**Gate P4:** 1080p loop video CPU/GPU and RAM numbers recorded (and compared with Wallpaper Engine if the owner has it installed).

---

## Phase 5 — Workspaces

**P5.1 Activation with rollback.** `WorkspaceModule`: snapshot previous system state (taskbar visibility, DND, volume, wallpaper pause) into `workspace_state.json`; apply new; roll back on switch/exit; recover after crash on next start. *Accept:* tests with fake system adapters; kill the process while a workspace is active → next start restores state.

**P5.2 Triggers and apps.** Switching from the window, the Island drawer, and `HotkeyIndex`; launch only apps not already running; publish `WorkspaceChanged`. *Accept:* Island shows the event; running apps are not duplicated.

**P5.3 Workspaces page.** Editor for every `WorkspaceConfig` field, batch launcher list with drag reorder. *Accept:* §9 checklist.

---

## Phase 6 — Widgets, Taskbar, and shell cleanup

**P6.1 `WidgetsModule` + `DesktopWidgetBase`.** Merge duplicate resource widgets; dragging, pinning, opacity; updates stop when hidden/suspended. *Accept:* CPU at idle ≈ 0% with widgets hidden.

**P6.2 Widgets.** Clock (analog+HUD, capsule), CPU/GPU/RAM with alert state (RAM > 88%) and motion (§6.6). *Accept:* hands tween smoothly; alert pulses twice then holds.

**P6.3 Widgets manager and Taskbar customizer.** Verify what exists; implement or defer with a `DECISIONS.md` note. *Accept:* settings persist and revert cleanly.

**P6.4 Final shell cleanup.** `MainWindow.xaml.cs` < 400 lines; remove all leftover wiring; repo-wide grep gate: zero `Dispatcher.Invoke`, `.Result`, `.Wait(`. *Accept:* grep output shown; app fully functional.

---

## Phase 7 — AI Assistant (last)

**P7.1 Module and permissions.** `AssistantModule`, permission engine by `AIPermissionLevel`, kill switch, audit to `audit.jsonl` (rotated). *Accept:* Level2 requires confirmation; Level3 blocked by default; kill switch stops everything < 1s.

**P7.2 Tools and model client.** Start with Level0/1 tools (open app, read CPU/RAM, change volume, apply workspace/wallpaper). Ollama client with timeouts and cancellation; cloud optional. *Accept:* each tool has tests with a fake system; failures never block the UI.

**P7.3 Assistant page.** Offline models, permissions, audit viewer. *Accept:* §9 checklist.

---

## Phase 8 — Polish

**P8.1 Visual QA pass** of every screen at widths 900/1100/1400 and scales 1.0/1.25, Arabic and English strings. **P8.2 Soak test** 4+ hours with periodic interaction; handles/private bytes plateau. **P8.3 Release script** (`dotnet publish`, shortcut to the published folder, `StartWithWindows` option verified). **P8.4 Docs** updated to match reality.

---

## Interaction stress test (used at each gate)

1. Start with all modules enabled; play music.
2. Change volume 50 times quickly; skip tracks 20 times; toggle mic mute 10 times.
3. Open/close Island expanded states 30 times; trigger USB connect/disconnect if possible.
4. Open the radial wheel 30 times (confirm 10, cancel 20).
5. Switch Workspaces 10 times; switch wallpaper 5 times.
6. Enter and leave a fullscreen video/game twice.
7. Pass if: UI never stalls (watchdog log empty), `app_debug.log` has no unhandled exceptions, handles grow < 5%, benchmark within budget.
