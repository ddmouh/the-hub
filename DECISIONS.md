# Architecture & Implementation Decisions

| Date | Decision | Reason | Status |
|---|---|---|---|
| 2026-10-13 | Ingest v2.0 specs into docs/ | Establish canonical reference documents for in-place migration | Completed |
| 2026-10-13 | P0.1: Baseline & native binaries | Native FFmpeg binaries kept for current runtime; lazy engine in Phase 4 | Completed |
| 2026-10-13 | P0.2: Authoritative dev run path | dev-run.bat enforces clean build & termination of stale instances | Completed |
| 2026-10-13 | P0.3: Eliminate dual host | TheHub.Overlays converted to Library, dead NamedPipeServer & Common/IPC deleted | Completed |
| 2026-10-13 | P0.4: Showcase as dev QA tool | Hidden from standard sidebar navigation; triggered via --showcase with automated PNG export | Completed |
| 2026-10-13 | P0.5: Settings golden test | Added sanitized settings.golden.json and 2 automated xUnit round-trip tests | Completed |
| 2026-10-13 | P0.6: Baseline measurements | EmptyWpf reference measured (42.4MB Priv / 70MB WS); perf-baseline.md established | Completed |
| 2026-10-13 | P1.1: TheHub.Design Library | Created dedicated design library with Tokens.xaml for colors, typography, radii, spacing | Completed |
| 2026-10-13 | P1.2: Motion Engine | Implemented pure SpringSimulation and CompositionTarget SpringAnimator auto-stopping at rest | Completed |
| 2026-10-13 | P1.3: HubBus Event Bus | Implemented non-blocking in-process bus with System.Threading.Channels and typed events | Completed |
| 2026-10-13 | P1.4: UiBridge UI Marshaling | Centralized InvokeAsync marshaling with high-frequency per-key coalescing | Completed |
| 2026-10-13 | P1.5: ModuleHost & PowerPolicy | Implemented IHubModule lifecycle, 3-retry fault tolerance, and fullscreen/battery suspend | Completed |
| 2026-10-13 | P1.6: Diagnostics & Watchdog | Added Breadcrumbs ring buffer and UiWatchdog monitoring UI stalls > 1.5s | Completed |
| 2026-10-13 | P1.7: Section Settings Split | Created SectionSettingsStore with atomic writes, migration, and section corruption isolation | Completed |
| 2026-10-13 | P1.8: Core UI Controls | Implemented SectionHeader, SettingRow, HubButton variants, HubToggle, and HubCard | Completed |
| 2026-10-13 | P2.1–P2.7: IslandModule & Services | Pure IslandStateMachine, bus monitors (Audio, Media, Usb, Downloads, Prayer, Pomodoro) | Completed |
| 2026-10-13 | P3.1–P3.4: RadialModule & Gestures | Pure RadialGestureRecognizer, RadialWheelCanvas bloom, live settings | Completed |
| 2026-10-13 | P4.1–P4.4: Wallpaper Engine | Lazy FlyleafLib loading, pause on fullscreen/battery, multi-monitor span | Completed |
| 2026-10-13 | P5.1–P5.4: Workspace Engine | Atomic plan generation (launch/kill/volume/wallpaper) & desktop snapshotting | Completed |
| 2026-10-13 | P6.1–P6.3: Widget Snap Engine | Pure WidgetSnapEngine (16px magnetic snap), polling suspension on fullscreen/battery | Completed |
| 2026-10-13 | P7.1–P7.4: AI Assistant & Sandbox | 4-tier security sandbox, sub-second emergency kill switch, audit trail in audit.jsonl | Completed |
| 2026-10-28 | Fix Startup Freeze & Hardware Widget Overlap | Eliminated synchronous Process counter scans on UI thread; disabled default widget auto-spawn | Completed |
| 2026-10-28 | Smart Close Button (Hold-to-Exit) | Single click hides to tray; ~1.1s hold terminates entire application and background overlays | Completed |
| 2026-10-28 | Architectural Re-alignment & Foundation Audit | Paused UI micro-tweaks to review foundational architecture; reconciled status vs reality | Completed |
| 2026-10-28 | P1.9 / P6.4: Shell Decoupling & Page Extraction | Extract pages from MainWindow.xaml into UserControls; bring MainWindow.xaml.cs < 450 lines | Completed |
| 2026-10-29 | HoldToConfirmButton & Zero-Latency Startup | Native spring physics HoldToConfirm button for Close [X] and destructive actions; async module init; unified widget state | Completed |
| 2026-10-28 | Single Source of Truth Documentation | Created canonical README.md, cleaned duplicate docs, updated ARCHITECTURE.md and PRD | Completed |
| 2026-10-29 | Design System Expansion: HubHoldButton, HubRangeSlider, HubPillTabs & Upgraded HubSlider/HubToggle | Implemented 3 new design system controls and 2 upgrades using pure mathematical models, SpringAnimator motion tokens, and full RTL/scale validation | Completed |
