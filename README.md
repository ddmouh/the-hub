# The Hub — Desktop Companion

> **High-Performance Desktop Experience Companion for Windows 10/11**  
> Built with modern .NET 8 WPF, zero-blocking event architecture, and physics-based motion.

---

## 1. Overview

**The Hub** replaces cluttered standalone desktop utilities with a unified, lightweight, and cohesive desktop operating layer:

* 🏝️ **Dynamic Island Overlay:** Context-aware morphing capsule pinned to your primary screen. Tracks media playback (Spotify, Chrome, YouTube), system volume, microphone mute, USB device connects/disconnects, downloads, prayer times (Umm al-Qura), and Pomodoro focus timers.
* ⚡ **Radial Launcher:** Ultra-low latency gesture/hotkey launcher (`Alt+Z` / Middle Mouse Hold) with smooth bloom animations and customizable multi-slice wheels.
* 🖥️ **Desktop Customization Studio:** Live animated and static wallpapers with hardware-accelerated video decoding (Flyleaf/FFmpeg). Features automatic 0% CPU pause when gaming or running on battery.
* 🗂️ **Workspaces & Environment Launcher:** Batch application launchers with desktop window snapshotting and atomic rollback.
* 📊 **HUD Hardware Widgets:** Modular, draggable desktop widgets for CPU load, GPU compute, RAM utilization, and clock with magnetic edge-snapping and automatic polling suspension.
* 🤖 **AI Assistant:** Sandboxed system assistant with 4-tier security permissions, command audit logging (`audit.jsonl`), and a sub-second emergency hardware kill switch.

---

## 2. System Architecture

The solution (`TheHub.sln`) is engineered around strict boundaries, non-blocking asynchronous event channels, and modular lifecycles:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                             TheHub.App (WinExe)                             │
│       Shell Host • MainWindow (Sidebar + Frame) • Application Pages         │
│               [Decomposed into independent Page UserControls]               │
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

### Core Tenets
1. **Zero UI-Thread Blocking:** System monitors (COM audio callbacks, media sessions, mouse hooks) publish events to `HubBus` on background channels without touching the WPF Dispatcher.
2. **`UiBridge` Marshaling:** High-frequency updates (e.g. volume changes) are coalesced per frame before dispatching to UI via `InvokeAsync`.
3. **Power Policy Suspension:** When entering a fullscreen application or game, heavy background tasks (video decoding loops, hardware counter polling) immediately suspend to ensure 0% CPU/GPU interference.
4. **Atomic Isolated Settings:** Configuration is split into discrete JSON files (`app.json`, `island.json`, `wallpaper.json`, etc.) under `%AppData%\TheHub\`. Writes are debounced and atomic (temp-write + atomic move) to guarantee zero file corruption.

---

## 3. Documentation Index

For in-depth architectural specifications and guidelines, refer to the canonical documents in the [`docs/`](docs/) directory:

| Document | Description |
|---|---|
| 📖 [**`docs/ARCHITECTURE.md`**](docs/ARCHITECTURE.md) | Complete system architecture, project boundaries, threading contracts, gap analysis, and roadmap. |
| 📋 [**`docs/PRD.md`**](docs/PRD.md) | Product Requirements Document (PRD v2.0) defining every feature, state machine, and acceptance criterion. |
| 🎨 [**`docs/DESIGN_SYSTEM.md`**](docs/DESIGN_SYSTEM.md) | Design tokens, color palette, typography rules, layout spacing, spring physics curves, and UI components. |
| 📝 [**`DECISIONS.md`**](DECISIONS.md) | Architectural Decision Records (ADR) tracking historical decisions, rationale, and live phase status. |
| 🛠️ [**`docs/IMPLEMENTATION_PLAN.md`**](docs/IMPLEMENTATION_PLAN.md) | Phased implementation plan, test requirements, and acceptance gates. |
| 📊 [**`docs/perf-baseline.md`**](docs/perf-baseline.md) | Performance budgets, memory consumption baselines, and resource measurement logs. |

---

## 4. Getting Started & Development

### Prerequisites
* Windows 10 (Build 19041+) or Windows 11
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* Visual Studio 2022 or VS Code with C# Dev Kit

### Building the Solution
```powershell
# Restore and build the complete solution
dotnet build TheHub.sln -c Debug
```

### Running Tests
The test suite contains 109 automated unit and stress tests covering event buses, state machines, gesture recognizers, and settings persistence:
```powershell
dotnet test TheHub.sln --logger "console;verbosity=minimal"
```

### Running The Hub
To start the application cleanly (terminating any running stale background instances first):
```powershell
.\dev-run.bat
```

To run with developer showcase snapshot export:
```powershell
dotnet run --project TheHub.App -- --showcase
```

---

## 5. Project Status & Roadmap

* [x] **Phase 0:** Stable baseline, native binaries isolation, and dev run paths.
* [x] **Phase 1:** `TheHub.Design` token library, spring motion engine, `HubBus`, `UiBridge`, and `ModuleHost`.
* [x] **Phase 2:** Dynamic Island state machine, priority preemption, system event services.
* [x] **Phase 3:** Radial Launcher gesture recognizer, canvas bloom motion, live profile switcher.
* [x] **Phase 4:** Wallpaper engine with lazy video loading and fullscreen pause rules.
* [x] **Phase 5:** Workspaces engine, process registry, and snapshot capture.
* [x] **Phase 6:** Desktop widget snap engine, hardware monitors, and power suspension.
* [x] **Phase 7:** AI Assistant 4-tier security sandbox, intent parser, and emergency kill switch.
* [x] **Bug Fixes:** Resolved startup freeze (~4.7s) and eliminated hardware widget coordinate collision.
* [x] **Smart Close:** Implemented hold-to-exit vs single-click-to-tray in header.
* [ ] **Phase 1.9 / 6.4 (Active):** Shell refactor — extracting monolithic `MainWindow.xaml` pages into independent `UserControl` views (`OverviewPage`, `WallpapersPage`, `IslandPage`, etc.) and reducing code-behind to < 400 lines.
