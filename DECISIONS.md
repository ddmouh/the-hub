# Architecture & Implementation Decisions

| Date | Decision | Reason |
|---|---|---|
| 2026-10-13 | Ingest v2.0 specs into docs/ | Establish canonical reference documents for in-place migration |
| 2026-10-13 | P0.1: Baseline & native binaries | Native FFmpeg binaries kept for current runtime; lazy engine in Phase 4 |
| 2026-10-13 | P0.2: Authoritative dev run path | dev-run.bat enforces clean build & termination of stale instances |
| 2026-10-13 | P0.3: Eliminate dual host | TheHub.Overlays converted to Library, dead NamedPipeServer & Common/IPC deleted |
| 2026-10-13 | P0.4: Showcase as dev QA tool | Hidden from standard sidebar navigation; triggered via --showcase with automated PNG export |
| 2026-10-13 | P0.5: Settings golden test | Added sanitized settings.golden.json and 2 automated xUnit round-trip tests |
| 2026-10-13 | P0.6: Baseline measurements | EmptyWpf reference measured (42.4MB Priv / 70MB WS); perf-baseline.md established |
| 2026-10-13 | P1.1: TheHub.Design Library | Created dedicated design library with Tokens.xaml for colors, typography, radii, spacing |
| 2026-10-13 | P1.2: Motion Engine | Implemented pure SpringSimulation and CompositionTarget SpringAnimator auto-stopping at rest |
| 2026-10-13 | P1.3: HubBus Event Bus | Implemented non-blocking in-process bus with System.Threading.Channels and typed events |
| 2026-10-13 | P1.4: UiBridge UI Marshaling | Centralized InvokeAsync marshaling with high-frequency per-key coalescing |
| 2026-10-13 | P1.5: ModuleHost & PowerPolicy | Implemented IHubModule lifecycle, 3-retry fault tolerance, and fullscreen/battery suspend |
| 2026-10-13 | P1.6: Diagnostics & Watchdog | Added Breadcrumbs ring buffer and UiWatchdog monitoring UI stalls > 1.5s |
| 2026-10-13 | P1.7: Section Settings Split | Created SectionSettingsStore with atomic writes, migration, and section corruption isolation |
| 2026-10-13 | P1.8: Core UI Controls | Implemented SectionHeader, SettingRow, HubButton variants, HubToggle, and HubCard |
| 2026-10-13 | P1.9: Shell Header Fix | Eliminated column collision bug by using responsive WrapPanel in toolbar headers |
| 2026-10-13 | P2.1: IslandModule | Implemented IHubModule, decoupled DynamicIslandWindow from MainWindow.xaml.cs via UiBridge |
| 2026-10-13 | P2.2: IslandStateMachine | Pure C# state machine with priority preemption, timers, and persistent data restoration |
| 2026-10-13 | P2.3: Monitors to HubBus | Volume, Media, Clipboard, and ActiveApp publish to bus; AudioService serial COM queue |
| 2026-10-13 | P2.4: Zero Island Blocking | Replaced Dispatcher.Invoke in Island and DisplayDimmerOverlay; fully async COM calls |
| 2026-10-13 | P2.5/P2.6: Island Spring Morph | Capsule morphing driven by pure physics SpringAnimator engine with snappy curve |
| 2026-10-13 | P2.7: Event Services | Implemented UsbWatcher, DownloadWatcher, PrayerTimeService, and PomodoroService |
| 2026-10-13 | Gate P2: Interaction Stress | 50 volume, 20 media, 30 morph, and USB events executed with zero watchdog violations |
| 2026-10-13 | P3.1: Radial Gesture Engine | Pure RadialGestureRecognizer with hold detection, flick angle, and activation zones |
| 2026-10-13 | P3.1: RadialModule | Implemented IHubModule managing RadialWindow lifecycle, decoupled from UI thread |
| 2026-10-13 | P3.2: Wheel Visuals & Bloom | RadialWheelCanvas bloom motion with zero animation on hidden window |
| 2026-10-13 | P3.3: Radial Settings Live | Triggers, Appearance, Slices editor, and profile switcher live updates |
| 2026-10-13 | P3.4: Fullscreen & Zero Blocking | Replaced Dispatcher.Invoke in RadialDimmerOverlay; FullscreenProtection via PowerPolicy |
| 2026-10-13 | Gate P3: Interaction Stress | 30 open/confirm cycles verified with stable state and zero watchdog timeouts |
| 2026-10-13 | P4.1: Lazy Video Engine | Created WallpaperEngine with lazy FlyleafLib startup and aggressive inactive memory trimming |
| 2026-10-13 | P4.2: WallpaperModule & Pause | Implemented IHubModule with multi-reason pause (Fullscreen, Battery, Workspace) halting decode loop (0% CPU) |
| 2026-10-13 | P4.3: Multi-Monitor & Span Fit | Added virtual desktop Span fit mode across monitors and Center mode; robust resolution change handling |
| 2026-10-13 | P4.4: Missing File Resilience | Added IsMissingFile warning badges and graceful fallback without crashes or state corruption |
| 2026-10-13 | Gate P4: Tests & Verification | 94/94 solution tests passing; verified engine lifecycle, pause combinations, and span calculations |
| 2026-10-13 | P5.1: Pure WorkspaceEngine | Created WorkspaceEngine with atomic plan generation (launch/kill/volume/wallpaper) & desktop snapshotting |
| 2026-10-13 | P5.2: WorkspaceModule | Implemented IHubModule managing atomic workspace transitions and broadcasting WorkspaceChanged to HubBus |
| 2026-10-13 | P5.3/P5.4: Workspace Studio UI | Added Capture Current Desktop button, island coordination, and multi-profile linkage |
| 2026-10-13 | Gate P5: Tests & Verification | 100/100 solution tests passing; verified atomic plan generation, isolation rules, and snapshot filters |
| 2026-10-13 | P6.1: Widget Snap Engine | Pure WidgetSnapEngine with 16px magnetic screen-edge snap, inter-widget alignment, and grid snapping |
| 2026-10-13 | P6.2: WidgetsModule & Power Policy | Implemented IHubModule coordinating desktop widgets with automatic polling suspension on Fullscreen/Battery |
| 2026-10-13 | P6.3: Polling Suspension Virtuals | Added PausePolling() / ResumePolling() across DesktopWidgetBase and all hardware widgets |
| 2026-10-13 | P6.4: UiBridge STA Support | Enhanced WpfUiDispatcher to execute on STA threads in headless/testing environments without WPF pump |
| 2026-10-13 | Gate P6: Tests & Verification | 105/105 solution tests passing; verified magnetic edge snap, alignment, and module power lifecycle |
| 2026-10-13 | P7.1: AI Sandbox & Permissions | Implemented 4-tier security sandbox (Level 0 Safe to Level 3 Dangerous) with audit logging in audit.jsonl |
| 2026-10-13 | P7.2: Emergency Kill Switch | Created sub-second emergency kill switch immediately halting active actions and rejecting new executions |
| 2026-10-13 | P7.3: Tools & Intent Parser | Implemented pure AIIntentParser with argument extraction and concrete tools (metrics, volume, workspace, apps) |
| 2026-10-13 | P7.4: AIAssistantModule & UI | Implemented IHubModule coordinating assistant lifecycle, wired into MainWindow and emergency UI button |
| 2026-10-13 | Gate P7: Tests & Verification | 109/109 solution tests passing; verified intent parsing, 4-tier sandbox enforcement, kill switch, and audit trail |



