# Performance Baseline — The Hub

Baseline measured on Windows 11 with .NET 8 WPF.

## 1. Reference: Minimal Empty WPF Application (`scratch/EmptyWpf`)
To understand the baseline cost imposed purely by the .NET 8 runtime and WPF presentation framework, a clean empty WPF window was measured:

| Metric | Empty WPF Baseline |
|---|---|
| Working Set (RAM) | **70.0 MB** |
| Private Bytes (RAM) | **42.4 MB** |
| Idle CPU | **0.0%** |
| Threads | **15** |
| OS Handles | **364** |

## 2. The Hub Current Baseline (All Modules, No Active Animated Wallpaper)
Measured across a 10-minute continuous benchmark (`scratch/run_bench_10min.ps1`):

| State | Working Set | Private Bytes | CPU % | Threads | Handles |
|---|---|---|---|---|---|
| **Startup** | 249.4 MB | 194.1 MB | ~10% (burst) | 28 | 951 |
| **Island/Player Interacted** | 251.4 MB | 183.5 MB | 0.0% | 28 | 963 |
| **10-Min Steady Idle** | 250.2 MB | 175.8 MB | 0.0% | 19 | 989 |

- **Net Marginal Cost of The Hub (above raw WPF):**
  - Private Bytes: ~133.4 MB (175.8 - 42.4)
  - Working Set: ~180.2 MB (250.2 - 70.0)
  - Handles: ~625 handles
  - UI Freezes: 0 detected

## 3. Module Marginal Cost Breakdown & Phase Targets

| Module | Primary Cost Driver | Baseline Marginal Private RAM | Target Budget (Phase 1-3) |
|---|---|---|---|
| **Core & Shell** | MainWindow, Native hooks, monitors, COM audio | ~45 MB | ≤ 30 MB |
| **Dynamic Island** | Transparent window, animations, sliders, media hooks | ~35 MB | ≤ 25 MB |
| **Radial Launcher** | Polar geometry canvas, icon cache, dimmer overlay | ~25 MB | ≤ 20 MB |
| **Widgets** | Clock, CPU, GPU, RAM polling timers | ~20 MB | ≤ 15 MB |
| **Video Wallpaper Engine** | FlyleafLib & native FFmpeg DLLs (avcodec, avformat) | Pre-loaded: ~50-80 MB | Lazy loaded (0 MB when unused) |

## 4. Gate Verification & Budgets
- **Phase 1 Target:** Private Bytes ≤ 150 MB (without animated wallpaper), Idle CPU ≤ 1%, 0 UI freezes.
- **Phase 3 Target:** Private Bytes ≤ 120 MB (without animated wallpaper).
- **Handle Leak Rule:** Handles must not drift > 5% over 30 minutes.
