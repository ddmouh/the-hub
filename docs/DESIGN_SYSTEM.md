# DESIGN SYSTEM — The Hub

Goal: one visual language and one motion language for every window. No screen may use a raw color, font size, margin, or animation duration that is not defined here. Numbers marked **(tune)** are starting values: the owner judges by eye in the running app and the agent updates this file, not the call sites.

Where it lives in code: `TheHub.Design` (new WPF library in the same solution): `Tokens.xaml`, `Motion/`, `Controls/`.

## 1. Principles

1. **Calm dark glass:** deep neutral backgrounds, one accent, soft borders, restrained glow.
2. **Hierarchy through size and contrast**, not through more colors.
3. **Everything moves with purpose:** springs for movement, short fades for state, no decorative loops.
4. **Consistency beats novelty:** same card, same row, same button everywhere.
5. **Arabic and English both first-class** (RTL-safe layouts; text never clipped).

## 2. Color tokens (extracted from the current UI, then normalized)

| Token | Value | Use |
|---|---|---|
| `Bg.Window` | `#0B0B0D` | window background |
| `Bg.Card` | `#141418` | cards, panels |
| `Bg.Control` | `#181820` | buttons, inputs |
| `Bg.Raised` | `#1C1E2A` | popups, selected rows |
| `Border.Subtle` | `#22222B` | card borders |
| `Border.Default` | `#2A2A38` | control borders |
| `Border.Hover` | `#3F3F50` | hover borders |
| `Text.Primary` | `#E4E4E7` | main text |
| `Text.Secondary` | `#A1A1AA` | labels |
| `Text.Tertiary` | `#71717A` | hints, disabled |
| `Accent` | `#3B82F6` | primary actions, active state |
| `Accent.Hover` | `#60A5FA` | |
| `Accent.Soft` | `#3B82F6` @ 16% | selected background |
| `Success` | `#10B981` | active, ready dot |
| `Warning` | `#F59E0B` | |
| `Danger` | `#EF4444` | kill switch, destructive |
| `Danger.Soft` | `#EF4444` @ 16% (`#29EF4444`) | destructive button tint background |
| `Violet` | `#7C3AED` | "LIVE" badges only |

Rules: max one accent color visible per surface; status colors only for status; per-workspace/profile `AccentColor` may tint that item's icon and ring only.

## 3. Typography

Font: `Segoe UI Variable Text, Segoe UI` (UI), user font via `GlobalFontFamily` when set. Mono for numbers/hex: `Cascadia Mono, Consolas`. Arabic uses the same stack (Segoe UI Variable supports Arabic).

| Token | Size / weight | Use |
|---|---|---|
| `Type.Display` | 28 / SemiBold | page hero numbers |
| `Type.Title` | 19 / Bold | page title |
| `Type.Section` | 14 / SemiBold | card title |
| `Type.Body` | 12.5 / Regular | default |
| `Type.Label` | 11.5 / Medium | control labels, buttons |
| `Type.Caption` | 10.5 / Regular | hints, badges |

All sizes multiply by the global `UiScale` setting. Titles are short (max ~28 characters); long text wraps or ellipsizes with a tooltip, never overlaps.

## 4. Spacing, shape, elevation

- **Grid:** 4px base. Spacing tokens: 4, 8, 12, 16, 24, 32.
- **Radius:** `Radius.S`=6, `Radius.M`=10, `Radius.L`=14, `Radius.XL`=20, `Radius.Pill`=999.
- **Elevation:** level 0 flat; level 1 = 1px `Border.Subtle`; level 2 (popups) = `Bg.Raised` + `Border.Default` + a single static soft shadow (never animated). No blur effects on live-updating areas.
- **Hit targets:** minimum 32x32 for any clickable element.

## 5. Components (in `TheHub.Design/Controls`)

Each component defines states: Rest, Hover, Pressed, Focused, Disabled (and Selected/Checked where relevant). Motion for each state is in §6.

| Component | Notes |
|---|---|
| `HubButton` (Primary, Secondary, Ghost, Danger, Icon) | height 32 (Compact 28); icon + label with 8px gap |
| `HubToggle` | 36x20 track, thumb slides with `Spring.Snappy` |
| `HubHoldButton` | Destructive confirm button; progressive linear fill over HoldDuration (1200ms default); clip-based inverted text; Spring.Snappy rollback on early release; ~1.2s confirmed success state. Variants: Danger, Neutral. |
| `HubRangeSlider` | Dual 16px thumbs on 4px track, Accent fill segment, floating value tooltip bubble, MinimumDistance clamping, arrow/Tab navigation. |
| `HubPillTabs` | Segmented pill container with sliding indicator animated via Spring.Smooth, RTL mirroring, and optional CollapseWhenIdle. |
| `HubSlider` | Upgraded single thumb on 4px track with animated value tooltip bubble appearing with Fade.Fast on drag/adjust. |
| `HubToggle` | Upgraded 36x20 track, thumb slides with Spring.Snappy, track color cross-fades Fade.Base, 1.15x press-stretch response. |
| `HubChip` / `FilterChip` | pill; selected = `Accent` fill |
| `HubCard` | `Bg.Card`, `Radius.L`, padding 16, optional header row (title + actions) |
| `HubComboBox` | Dark tokenized dropdown (`Bg.Control`, `Border.Default`, `Radius.M`, `Bg.Raised` popup) replacing native white dropdowns |
| `SettingRow` | label + description (left), control (right), min height 48; collapses to stacked layout under 560px |
| `SectionHeader` | title + optional subtitle + right-aligned actions; **uses a wrapping layout, never fixed columns** |
| `ModuleCard` | module name, state dot, enable toggle, quick stats (RAM/CPU), "Open settings" |
| `ColorPicker` | existing control, restyled with tokens |
| `Tooltip`, `Toast`, `Dialog` | Dialog = scrim + `Bg.Raised`, bilingual labels supported |
| `PreviewTile` | for wallpapers/screens; hover shows actions |

**Layout rules (prevent overlap):** page header = `WrapPanel`/`Grid` with a `*` column for the title (min width 240) and an `Auto` column for actions that wraps under the title below 720px. Any row with more than three actions uses an overflow menu. Every page is verified at widths 900, 1100, 1400 and at UiScale 1.0 and 1.25.

## 6. Motion system

### 6.1 Principles and rules
- Motion communicates cause and effect: it starts from the thing the user touched and ends where the result lives.
- **Springs** for position/scale/shape; **short eased fades** for opacity/color.
- Every animation is **interruptible**: a new target mid-flight continues from the current value and velocity (no snapping, no restart).
- Respect `PerformanceMode` and Windows "reduce animations": replace springs with 120ms fades.
- Performance rules: animate `RenderTransform` (translate/scale), `Opacity`, and the Island capsule's own size/radius only. Never animate blur or drop-shadow. No animation runs while the window is hidden or the module is suspended.

### 6.2 Tokens (tune)

| Token | Type | Values |
|---|---|---|
| `Spring.Snappy` | spring | stiffness 520, damping 34, mass 1 (controls, toggles, press) |
| `Spring.Smooth` | spring | stiffness 320, damping 30, mass 1 (panels, drawers) |
| `Spring.Bouncy` | spring | stiffness 420, damping 20, mass 1 (island expand, wheel open) |
| `Spring.Gentle` | spring | stiffness 220, damping 26, mass 1 (large surfaces) |
| `Fade.Fast` / `Fade.Base` / `Fade.Slow` | tween | 100 / 160 / 260 ms |
| `Ease.Out` | cubic-bezier | (0.2, 0.0, 0.0, 1.0) |
| `Ease.InOut` | cubic-bezier | (0.4, 0.0, 0.2, 1.0) |
| `Stagger` | delay | 40 ms per item, max 6 items |
| `Press` | scale | 0.96 |
| `Hover` | lift | translateY −1px and scale 1.02 over `Spring.Snappy` |

Engine: `SpringAnimator` in `TheHub.Design/Motion` (critically-damped/underdamped solver driven by `CompositionTarget.Rendering` at a single shared tick, auto-stops at rest). API sketch: `element.SpringTo(Property.TranslateY, 0, Spring.Smooth)`, with value and velocity preserved when retargeted.

### 6.3 Standard interactions
- **Button:** hover = lift + border brighten (`Fade.Fast`); press = scale 0.96 (`Spring.Snappy`); release returns.
- **Toggle:** thumb slides (`Spring.Snappy`), track color cross-fades (`Fade.Base`).
- **Page change:** outgoing fades 100ms; incoming content translates up 12px → 0 with `Spring.Smooth` while fading in; items stagger by `Stagger`.
- **Card appear:** opacity 0→1 and translateY 8→0.
- **List reorder/add/remove:** neighbors slide with `Spring.Smooth`; removed item shrinks and fades 160ms.
- **Dialog:** scrim fades `Fade.Base`; dialog scales 0.96→1 and fades with `Spring.Smooth`.

### 6.4 Dynamic Island motion (primary showpiece)

Structure: fixed-size transparent host window (size of the largest state) with one capsule element inside. The capsule's `Width`, `Height`, and `CornerRadius` are animated by springs; the host never resizes during animation.

| Transition | Capsule shape | Content |
|---|---|---|
| Idle → Compact event | grows horizontally to content width, height stays | glyph/title fade+slide in (`Fade.Base`, 40ms delay) |
| Compact → Expanded | width/height/radius spring (`Spring.Bouncy`), radius 22→28 | compact content fades out 80ms first; expanded blocks stagger in by `Stagger` |
| Expanded → Compact/Idle | reverse with `Spring.Smooth` | expanded content fades out first (100ms), then shrinks |
| Event interrupts event | no close; capsule retargets to the new size mid-flight | old content cross-fades out while new content slides up 6px |
| Volume | width constant; fill bar animates with `Spring.Snappy` per change | number counts with 80ms fade |
| Music waves | 5 bars, heights driven by a lightweight timer at 20fps (or audio levels if available) | stops when paused or hidden |
| USB/Prayer | capsule gives a subtle scale pulse 1.0→1.03→1.0 once (`Spring.Bouncy`) | glyph "pops" 0.6→1 |
| Pomodoro | progress ring sweeps continuously; last 10s pulses | |
| Dismiss | capsule retracts to Idle shape; opacity of content 0 | |

Timing: show duration `IslandDurationMs` (default 2500); hover pauses the dismiss timer; drag follows the pointer 1:1 and settles with `Spring.Smooth` on release.

### 6.5 Radial Launcher motion
- **Open:** wheel scales 0.85→1 and fades in with `Spring.Bouncy`; slices fan out from the center with `Stagger` (max 6 delay steps); background dimming fades in `Fade.Base` to `BackgroundDimming`.
- **Targeting:** the slice in the pointer's direction scales to 1.12 with `Spring.Snappy`; its label fades in; neighbors dim to 70%. Changing slices retargets mid-flight.
- **Confirm (release):** chosen slice scales to 1.2 and fades while the wheel collapses 0.9 + fade (`Fade.Fast`); app launch starts at the same time.
- **Cancel:** wheel collapses to center (`Spring.Smooth`).
- Must start rendering within 150ms of the trigger; the first frame uses no animation delay.

### 6.6 Other surfaces
- **Wallpaper switch:** cross-fade between old and new frame (`Fade.Slow`); no flash of desktop.
- **Widgets:** clock hands tween per tick (no jump); hardware widgets animate value changes with `Fade.Base` and a number roll; alert state pulses border color 2 times, then holds.
- **Workspace switch:** the active ring moves to the new item with `Spring.Smooth`; the Island shows the `WorkspaceChanged` event.
- **Toast:** slides in from bottom-right 16px with `Spring.Smooth`, leaves with `Fade.Base`.

## 7. Shell layout

- Window: 1100x720 default, min 900x600. Sidebar 220px (collapsible to 64px icons). Content max width 1200 with 24px padding.
- Sidebar items: icon + label, 40px high, selected = `Accent.Soft` + 3px accent bar that slides between items (`Spring.Smooth`).
- Header region of each page: title (`Type.Title`) + one-line description (`Type.Caption`) + actions.
- Footer of the sidebar: kill switch (`Danger`, Ghost) and save status (autosave indicator; the manual "Save Settings" button becomes unnecessary).

## 8. Screens inventory (rebuild order follows `IMPLEMENTATION_PLAN.md`)

Overview · Wallpapers · Dynamic Island · Radial Launcher (Triggers, Appearance, Slices) · Workspaces · Widgets · Taskbar · Assistant · Options (Typography/UI scale, Custom fonts, Monitors) · Modals (Icon picker, Installed apps, Exit prompt (AR/EN), Color picker).

Each screen spec in code review must include: the tokens used, widths tested, and layout verification at target scales.

## 9. Visual QA checklist (every UI task)

- [ ] No raw colors, sizes, margins, or durations in XAML outside token files.
- [ ] No overlap at widths 900/1100/1400 and UiScale 1.0/1.25; Arabic text length does not break layout.
- [ ] All states exist: rest, hover, pressed, focused, disabled.
- [ ] All motion uses tokens; retargeting mid-animation looks correct.
- [ ] Animations stop when the window is hidden or the module is suspended.
- [ ] Controls verified directly in live application pages at 900/1100/1400 widths.
