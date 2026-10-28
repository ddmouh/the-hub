# ARCHITECTURE — The Hub

## 1. Executive Summary & Vision

**The Hub** is an integrated, high-performance Desktop Experience Companion for Windows 10/11 built on .NET 8 WPF. It consolidates multiple desktop customization utilities into a single, cohesive, resource-efficient system:
1. **Dynamic Island Overlay** (context-aware morphing capsule for media, volume, mic, notifications, prayer times, pomodoro).
2. **Radial Launcher** (low-latency gesture/hotkey-driven application and workspace wheel).
3. **Hardware & Desktop Widgets** (independent, draggable, snap-aligned HUD widgets for CPU, GPU, RAM, Clock).
4. **Live & Static Wallpaper Engine** (multi-monitor video/image engine with hardware-accelerated pause on fullscreen/battery).
5. **Workspaces & Environment Launcher** (atomic multi-app snapshotting, launching, process isolation, and rollback).
6. **AI Assistant with System Permissions** (sandboxed 4-tier intent parser, audit trail, and hardware emergency kill switch).

---

## 2. Solution Structure & Project Boundaries

The solution (`TheHub.sln`) comprises 6 focused projects enforcing strict separation of concerns:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                             TheHub.App (WinExe)                             │
│       Shell Host • MainWindow (Sidebar + Frame) • Application Pages         │
│          [Under Refactoring: Extracting monolithic code into Pages]         │
└───────────────────────▲──────────────────────────────────────▲──────────────┘
                        │ Data / Commands                      │ Views / Windows
┌───────────────────────┴──────────────────┐   ┌───────────────┴──────────────┐
│ TheHub.Core (Library)                    │   │ TheHub.Overlays (Library)    │
│  • HubBus (Channels-based Typed Bus)     │◄──┤  • DynamicIslandWindow       │
│  • UiBridge (Coalesced UI Marshaling)    │   │  • RadialWindow & Canvas     │
│  • ModuleHost & IHubModule Lifecycle     │   │  • Desktop Widgets (Clock/HW)│
│  • PowerPolicy (Fullscreen/Battery)      │   │  • WallpaperWindow & Engine  │
│  • Monitors & System Hooks               │   └───────────────▲──────────────┘
│  • AIAssistant, Diagnostics & Watchdog   │                   │ Tokens & Controls
└───────────────────────▲──────────────────┘   ┌───────────────┴──────────────┐
                        │ Models & Settings    │ TheHub.Design (WPF Library)  │
┌───────────────────────┴──────────────────┐   │  • Tokens.xaml (Design Tokens│
│ TheHub.Common (Library)                  │   │  • Motion Engine (Spring)    │
│  • SectionSettingsStore (Atomic Split)   │   │  • Core UI Controls          │
│  • Domain Models & Logging               │   └──────────────────────────────┘
└──────────────────────────────────────────┘
```

### Project Responsibilities

| Project | Type | Role & Boundaries |
|---|---|---|
| **`TheHub.Common`** | Library | Shared domain models (`AppSettings`, `WorkspaceConfig`, `WallpaperItem`), atomic section settings store (`SectionSettingsStore`), and zero-dependency logging (`AppLogger`). Does **not** reference WPF UI libraries. |
| **`TheHub.Design`** | WPF Library | Shared visual foundation: color brushes, typography, corner radii, spacing tokens (`Tokens.xaml`), physics-based animation (`SpringAnimator`), and reusable controls (`SectionHeader`, `SettingRow`, `HubButton`). |
| **`TheHub.Core`** | Library | In-process, non-blocking orchestration engine: `HubBus` event bus, `UiBridge` UI marshaler, `ModuleHost`, `PowerPolicy`, system hooks (mouse/keyboard), system monitors (audio, media, clipboard, window states), and `AIAssistantModule`. |
| **`TheHub.Overlays`** | Library | Desktop overlay windows and visual modules: `IslandModule`, `RadialModule`, `WidgetsModule`, `WallpaperModule`. Owns the overlay windows, rendering them above other desktop windows without blocking the host process. |
| **`TheHub.App`** | WinExe | Application entry point and primary desktop dashboard window (`MainWindow`). Hosts the navigation sidebar and views. |
| **`TheHub.Tests`** | Test Project | Comprehensive automated test suite (109 xUnit unit and integration tests) verifying bus performance, state machines, isolation, settings persistence, and stress scenarios. |

---

## 3. Core Architectural Contracts

### 3.1 Module Lifecycle (`IHubModule` & `ModuleHost`)
Every feature area operates as an independent module conforming to `IHubModule`:
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
* **Fault Tolerance:** `ModuleHost` manages module instances. If a module encounters an unhandled exception, it is restarted up to 3 times before being isolated as `Faulted`. A failing module never crashes the entire application.
* **Power Policy Integration:** `PowerPolicy` listens to `FullscreenChanged` and `PowerStateChanged` bus events, automatically calling `Suspend()` on heavy rendering modules (e.g., video wallpapers, hardware polling) to ensure 0% background overhead during gaming or battery operation.

### 3.2 Non-Blocking Event Bus (`HubBus`) & UI Marshaling (`UiBridge`)
To eliminate UI freezes and deadlock hazards from Win32/COM callbacks:
* **`IHubBus`:** Built on `System.Threading.Channels` (unbounded single-reader channel). Background monitors, audio endpoints, and hooks publish events instantaneously without locking or awaiting UI threads.
* **`UiBridge`:** The single designated gateway to the WPF `Dispatcher`. It receives bus events, coalesces rapid updates (e.g., volume slider scrub or audio progress) per frame, and dispatches to UI components using `Dispatcher.InvokeAsync(..., DispatcherPriority.Normal)`.
* **Zero UI Blocking Rule:** No component outside window code-behind may touch `Dispatcher`. Synchronous blocking calls (`.Result`, `.Wait()`, `Dispatcher.Invoke`) are strictly forbidden across the codebase.

### 3.3 Atomic Sectional Settings Storage
Settings are split into discrete, human-readable JSON files stored in `%AppData%\TheHub\`:
* `app.json`: General app behavior, startup, theme, UI scale.
* `island.json`: Dynamic Island geometry, behaviors, and active widgets.
* `radial.json`: Radial wheel trigger keys, profiles, and shortcuts.
* `wallpaper.json`: Active wallpapers, monitor assignments, pause rules.
* `workspaces.json`: Environment workspace definitions and app lists.
* `widgets.json`: Widget visibility, desktop coordinates, and pin states.
* `assistant.json`: AI models, prompt templates, and security permission tiers.
* `audit.jsonl`: Immutable append-only audit log for all automated actions.

Writes are atomic (write to `.tmp` followed by `File.Move` replace) and debounced (500ms) to protect against corruption during power loss or abrupt shutdowns.

---

## 4. Current State vs. Architectural Gap Analysis

While the underlying engine, modules, and test suite are robust (109/109 tests passing), an honest audit reveals a critical architectural gap in the presentation tier:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ ARCHITECTURAL GOAL:                                                         │
│ MainWindow (< 400 lines) ─── Frame ───► Extracted Pages (UserControls)     │
│   ├── OverviewPage.xaml       ├── WallpapersPage.xaml ├── IslandPage.xaml   │
│   ├── RadialPage.xaml         ├── WorkspacesPage.xaml ├── WidgetsPage.xaml  │
│   └── AssistantPage.xaml      └── OptionsPage.xaml                          │
├─────────────────────────────────────────────────────────────────────────────┤
│ CURRENT REALITY:                                                            │
│ MainWindow.xaml (2,358 lines) ─── Monolithic God Window                     │
│ MainWindow.xaml.cs (3,519 lines) ─── 180 hardcoded controls & click handlers│
└─────────────────────────────────────────────────────────────────────────────┘
```

### Identified Gaps:
1. **The God Window Problem:** `MainWindow.xaml.cs` currently acts as a monolithic container holding 3,519 lines. It directly instantiates overlay windows alongside their respective modules, creating duplicate instances and layout bugs.
2. **Page Decoupling:** Pages must be extracted into standalone `UserControl` views with dedicated code-behind or ViewModels, reducing `MainWindow` to a pure navigation shell under 400 lines.
3. **ModuleHost Unification:** `MainWindow` should not manage overlay lifetimes directly; `ModuleHost` must serve as the authoritative orchestrator.
4. **Design Token Adoption:** Legacy inline brushes, borders, and button templates across pages must be replaced with `TheHub.Design` controls (`SectionHeader`, `SettingRow`, `HubButton`).

---

## 5. Architectural Remediation Roadmap

To align the codebase with this architecture, execution proceeds in 3 structured phases:

### Phase A: Documentation & Architectural Truth (Immediate)
- Establish canonical documentation in `docs/` (`README.md`, `ARCHITECTURE.md`, `PRD.md`, `DESIGN_SYSTEM.md`, `DECISIONS.md`).
- Eliminate duplicate files and reconcile progress status.
- Commit and push to Git.

### Phase B: Shell & Page Decomposition (Next)
- Create `TheHub.App/Pages/` directory.
- Extract view sections from `MainWindow.xaml` into dedicated `UserControl` components:
  1. `OverviewPage.xaml` (System metrics, status, quick toggles)
  2. `WallpapersPage.xaml` (Monitor layout, wallpaper grid, import, pause rules)
  3. `IslandPage.xaml` (Dynamic Island configuration, event toggles, scaling)
  4. `RadialPage.xaml` (Wheel slice editor, trigger settings, profile manager)
  5. `WorkspacesPage.xaml` (Batch launcher, app definitions, desktop snapshot)
  6. `WidgetsPage.xaml` (Hardware widget toggles, positions, snapping)
  7. `AssistantPage.xaml` (AI model selection, permissions, audit viewer)
  8. `OptionsPage.xaml` (Startup behavior, hotkeys, performance profile)
- Refactor `MainWindow.xaml` to host a navigation sidebar and a `Frame` / `ContentControl`.
- Reduce `MainWindow.xaml.cs` to < 400 lines.

### Phase C: ModuleHost Unification & Verification
- Wire all module startups strictly through `ModuleHost`.
- Eliminate duplicate widget and window instantiation.
- Execute full test suite (`dotnet test`) and runtime verification (`dev-run.bat`).
- Commit and push after every step.
