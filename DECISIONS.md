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

