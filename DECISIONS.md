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

