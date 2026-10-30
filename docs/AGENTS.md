# AGENTS.md — The Hub (in-place refactor and redesign)

You are working on the **existing** repository (`TheHub.sln`). Do not create a new project from scratch and do not rewrite everything at once. You improve the same codebase task by task.

Read before any work: `PRD.md` (what), `ARCHITECTURE.md` (how), `DESIGN_SYSTEM.md` (look and motion), `IMPLEMENTATION_PLAN.md` (task list). If documents disagree, authority order: `IMPLEMENTATION_PLAN.md` task text > `ARCHITECTURE.md` > `DESIGN_SYSTEM.md` > `PRD.md`. Record the conflict in `DECISIONS.md`.

## 1. Owner and communication

- The owner is a single Arabic-speaking user. **Write reports to the owner in Arabic**; keep code, comments, commit messages, and docs in English.
- Do not ask the owner questions. Decide, pick the simplest option that satisfies the documents, and log it in `DECISIONS.md` (date, decision, reason).
- Never claim something works without evidence (build output, test output, benchmark numbers, screenshots).

## 2. Workflow for every task

1. Work on branch `phase-<n>-<short-name>`; one task = one commit (or a few small ones). Message format: `P1.3: add HubBus with channel reader`.
2. Before editing: read the task in `IMPLEMENTATION_PLAN.md` and open every file it lists. Do not touch files outside its scope.
3. After editing, always:
   - `dotnet build TheHub.sln -c Debug` → 0 errors.
   - `dotnet test TheHub.sln` → all pass (new logic needs tests).
   - Run `dev-run.bat`. **Confirm the build number in the window title matches the build you just made.** If not, stop and fix the run path first.
   - UI tasks: verify design tokens, layouts across target widths and scale factors.
   - Module tasks: run `scratch\run_bench_10min.ps1` and add the before/after table.
4. Write a status note at the end of the task (Arabic, max 8 lines): what changed, evidence, measured numbers, what was deferred, risks.
5. Never mark a task done if any acceptance check fails. Say which failed.

## 3. Hard rules (violations = reject the change)

**Threading**
- No `Dispatcher.Invoke`, `.Result`, `.Wait()`, `GetAwaiter().GetResult()`. UI marshaling happens only in `UiBridge` using `InvokeAsync`.
- COM/native callbacks may only call `bus.Publish(...)` and return.
- Windows are created by their own module on the UI thread, never in `MainWindow` or in another module.

**Structure**
- No new code in `MainWindow.xaml.cs`. When you touch it, move logic into a ViewModel or module and delete the old code in the same commit.
- No file over 400 lines after its task completes. No class with more than one responsibility.
- Each module implements `IHubModule`, can be enabled/disabled independently, and communicates through `IHubBus`.
- Settings are per-section files (`ARCHITECTURE.md` §5). After migration, never write to `settings.json`.

**UI**
- XAML may use only tokens from `TheHub.Design`. No raw hex colors, raw font sizes, raw millisecond durations, or hand-written storyboards: use `SpringAnimator` and motion tokens.
- Layouts must wrap or ellipsize; never put fixed-width controls in an `Auto` column beside a star column without a minimum and wrap behavior.
- Animate only `RenderTransform`, `Opacity`, and the Island capsule's own size/radius. No animated blur or shadow.

**Safety and scope**
- No features beyond the plan. No new NuGet package without a note in `DECISIONS.md`.
- Never delete user data. Settings migration keeps the original as `settings.json.migrated`.
- The AI assistant is last. Do not expand its permissions; the kill switch must always work.
- Do not commit binaries (`bin`, `obj`, `.vs`, `TheHub-App`, native FFmpeg DLLs, `.venv`, `Screenshots`).

## 4. Commands

| Purpose | Command |
|---|---|
| Build | `dotnet build TheHub.sln -c Debug` |
| Test | `dotnet test TheHub.sln` |
| Run (always use this) | `dev-run.bat` (kills running instance, builds, runs; fails on build error) |
| Benchmark | `powershell -ExecutionPolicy Bypass -File scratch\run_bench_10min.ps1` |

## 5. Definition of done (per task)

- [ ] Acceptance checks in the task pass, with evidence.
- [ ] Build 0 errors, tests pass, no new warnings.
- [ ] Build number verified in the running window.
- [ ] Hard rules in §3 respected (grep changed files for `Dispatcher.Invoke`, `.Result`, `.Wait(`).
- [ ] UI tasks: `DESIGN_SYSTEM.md` §9 checklist ticked with screenshots.
- [ ] Docs updated if behavior or structure changed.
- [ ] Status note written.

## 6. When blocked

- Build error you cannot fix in 3 attempts: stop, restore the last good state of that file, and report the exact error.
- Unexpected behavior: add temporary logging, reproduce, report. Do not guess-patch several places.
- Budget breach in a measurement: report the numbers and the suspected cause. Do not hide it by changing the budget.
