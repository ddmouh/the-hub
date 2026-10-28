# The Hub — Multi-Section UI & Architectural Fix Plan

## Background

This plan covers 6 major sections of fixes across The Hub WPF application. The codebase uses a single-assembly WPF app (`TheHub.App`) with overlays in `TheHub.Overlays`, core services in `TheHub.Core`, and shared models in `TheHub.Common`. Changes touch MainWindow.xaml/cs, OverviewView.xaml, DynamicIslandWindow.xaml/cs, RadialDimmerOverlay.cs, RadialWindow.xaml.cs, DesktopClockWidget.xaml/cs, and new widget files.

---

## Open Questions

> [!IMPORTANT]
> **Section 2.3 — Hardware Widget Splitting**: The existing `DesktopResourceWidget` is a combined HUD overlay. Splitting it into 3 independent widgets (CPU, GPU, RAM) requires new `DesktopCpuWidget`, `DesktopGpuWidget`, and `DesktopRamWidget` classes each inheriting `DesktopWidgetBase`. Should these each have their own `Window` lifecycle, or should they share a host process singleton (like the current resource widget)?
>
> **Recommendation**: Independent windows (same pattern as the existing clock widget). Each can be independently toggled/dragged.

> [!IMPORTANT]
> **Section 2.4 — Universal Color Picker**: A full-spectrum color picker (Saturation/Brightness canvas + Hue slider + Hex/RGB inputs) is a significant custom control. Should this be implemented as a reusable `UserControl` in `TheHub.App/Controls/`? Or as a `Popup`-based control that appears inline when clicking a color swatch?
>
> **Recommendation**: Implement as a `ColorPickerPopup` UserControl in `TheHub.App/Controls/` that replaces the dot swatches in OverviewView.

> [!IMPORTANT]  
> **Section 6.3 — Font Registration**: Runtime font loading into `Application.Current.Resources` in WPF requires the fonts to be loaded as a `FontFamily` with a `file:///` URI. This works at runtime but does **not** persist across restarts unless the font file path is saved in `settings.json`. Is the current `SettingsManager` in `TheHub.Common.Config` the right place to persist the active font family name?
>
> **Recommendation**: Yes — add `GlobalFontFamily` and `CustomFonts` list to `AppSettings`.

---

## Proposed Changes

---

### SECTION 1: Wallpapers Tab (MainWindow.xaml + MainWindow.xaml.cs + WallpaperService.cs)

#### [MODIFY] [MainWindow.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml)

**Line 587** — Change header title text:
```diff
-  <TextBlock Text="TARGET DISPLAY &amp; MULTI-MONITOR ASSIGNMENT" FontSize="11.5" FontWeight="Bold" Foreground="#FFFFFF"/>
+  <TextBlock Text="TARGET DISPLAY" FontSize="11.5" FontWeight="Bold" Foreground="#FFFFFF"/>
```

**Lines 580–596** — Fix layout: Replace the `StackPanel` wrapping the title/buttons with a proper `Grid` split so the title doesn't overlap the action buttons. The existing structure already uses a 2-column Grid; no wrapping issue exists *structurally* but the title is too long and wraps over the buttons in narrow windows. Shortening the title to "TARGET DISPLAY" resolves the overlap.

**Lines 561–562** — The `ChkPauseWallpaperFullscreen` and `ChkPauseWallpaperBattery` checkboxes already fire `OnWallpaperSettingChanged`. Wire them `TwoWay` to `AppSettings` properties and add actual fullscreen/battery logic in the code-behind.

#### [MODIFY] [MainWindow.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml.cs)

- Add `FullscreenDetectionTimer` (`DispatcherTimer`, 1s interval) using `Win32Api.GetForegroundWindow()` + `GetWindowRect` vs. monitor bounds to detect fullscreen apps.
- Add `SystemEvents.PowerModeChanged` subscription to detect battery state via `SystemInformation.PowerStatus.PowerLineStatus`.
- `OnWallpaperSettingChanged` handler: save `PauseWallpaperOnFullscreen` and `PauseWallpaperOnBattery` to `AppSettings` and call `_wallpaperManager.SetPauseOnFullscreen(bool)` / `SetPauseOnBattery(bool)`.

#### [MODIFY] [WallpaperService.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.Core/Services/WallpaperService.cs)

- Add `bool PauseOnFullscreen` and `bool PauseOnBattery` public properties.
- On fullscreen detection: suspend `MediaElement` playback by calling `Pause()` on the active player.
- On battery: same.

---

### SECTION 2: Widgets Tab

#### 2.1 — Mouse Scroll Fix

#### [MODIFY] [OverviewView.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/OverviewView.xaml)

- Wrap the top-level content panel in a `ScrollViewer` with `CanContentScroll="False"`, `Focusable="True"`.
- Add a `PreviewMouseWheel` handler on the inner container to bubble scroll events to the outer `ScrollViewer` when inner controls consume them.

#### 2.2 — Clock Widget Trimming

#### [MODIFY] [DesktopClockWidget.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/Widgets/DesktopClockWidget.xaml)

- Remove `Row 2` (FooterRow) containing the date container and the "ONLINE" `TxtStatus` TextBlock entirely.
- Set `MainBorder` `Margin="2"` (reduced from 4) and reduce inner `Grid Margin` to `"6,4,6,4"`.
- Keep `SizeToContent="WidthAndHeight"` (already set).
- Remove the date grid row definition.

#### [MODIFY] [DesktopClockWidget.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/Widgets/DesktopClockWidget.xaml.cs)

- Remove all references to `TxtDate`, `TxtStatus`, `DateContainer`, `FooterRow`.
- Ensure visual style `ComboBox` `SelectedItem` change calls `OnPropertyChanged` and applies `DynamicResource` theme keys.

#### 2.3 — Hardware Widget Splitting

#### [NEW] [DesktopCpuWidget.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/Widgets/DesktopCpuWidget.xaml)
#### [NEW] [DesktopCpuWidget.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/Widgets/DesktopCpuWidget.xaml.cs)
#### [NEW] [DesktopGpuWidget.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/Widgets/DesktopGpuWidget.xaml)
#### [NEW] [DesktopGpuWidget.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/Widgets/DesktopGpuWidget.xaml.cs)
#### [NEW] [DesktopRamWidget.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/Widgets/DesktopRamWidget.xaml)
#### [NEW] [DesktopRamWidget.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/Widgets/DesktopRamWidget.xaml.cs)

Each widget:
- Extends `DesktopWidgetBase`
- Compact, dark-glass style (no neon fills by default — use muted accent)
- ProgressBar + percentage text + label
- Independently draggable, independently toggled from OverviewView

#### [MODIFY] [OverviewView.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/OverviewView.xaml)

- Replace the combined "System HUD" toggle card with 3 independent cards: CPU Load Widget, GPU Load Widget, RAM Usage Widget.
- Each card has its own toggle (Enable/Show), color indicator, and position info.
- Remove the monolithic `DesktopResourceWidget` toggle; replace with the 3 new ones.

#### [MODIFY] [MainWindow.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml.cs)

- Replace `_resourceWidget` field with `_cpuWidget`, `_gpuWidget`, `_ramWidget`.
- Wire up toggle handlers for each independently.

#### 2.4 — Universal Color Picker

#### [NEW] [ColorPickerControl.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/Controls/ColorPickerControl.xaml)
#### [NEW] [ColorPickerControl.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/Controls/ColorPickerControl.xaml.cs)

A self-contained `UserControl` with:
- A `Canvas` for Saturation/Brightness (HSB picker)
- A horizontal `Hue` `Slider`
- `TextBox` for Hex input
- `TextBox` pair for R, G, B
- `SelectedColor` dependency property with `ColorChanged` event

Replace the dot-swatch `StackPanel` buttons in `OverviewView.xaml` with `<controls:ColorPickerControl>`.

#### 2.5 — Navigation Re-categorization

#### [MODIFY] [MainWindow.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml)

- **Line 544**: Remove the "📊 System HUD" `RadioButton` tab. Change `UniformGrid Columns="4"` to `Columns="3"` for the Overview sub-tabs.
- The System HUD content (lines 794–891) remains in place but is now accessible from the Widgets tab as sub-sections.
- **Taskbar & Dock** tab remains as-is.

---

### SECTION 3: Workspaces Icon Contrast Fix

#### [MODIFY] [MainWindow.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml)

**Lines 2143–2146** — Icon picker tile button content:

The `IconSelectorButtonStyle` (lines 181–205) has no explicit `Foreground` setter. The default system foreground is black, causing dark-on-dark rendering.

**Fix**: Add `Foreground="#E0E0E0"` to `IconSelectorButtonStyle` setters, and brighten hover:
```diff
  <Style x:Key="IconSelectorButtonStyle" TargetType="Button">
      <Setter Property="Background" Value="#18181B"/>
+     <Setter Property="Foreground" Value="#E0E0E0"/>
      ...
      <Trigger Property="IsMouseOver" Value="True">
          <Setter TargetName="bd" Property="BorderBrush" Value="#3B82F6"/>
-         <Setter TargetName="bd" Property="Background" Value="#222226"/>
+         <Setter TargetName="bd" Property="Background" Value="#2E2E36"/>
+         <Setter Property="Foreground" Value="#FFFFFF"/>
      </Trigger>
```

---

### SECTION 4: Radial Launcher Fixes

#### 4.1 — Multi-Monitor Coordinate & Dimming Fix

#### [MODIFY] [RadialWindow.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/RadialLauncher/RadialWindow.xaml.cs)

**`ShowAt(int screenX, int screenY)`**: Replace the naive `SM_CXSCREEN` DPI scaling with monitor-aware positioning:
- Use `Win32Api.MonitorFromPoint` to get the HMONITOR for the cursor position.
- Call `Win32Api.GetMonitorInfo` to get the monitor's working area in physical pixels.
- Convert physical to DIP using the monitor's DPI (via `GetDpiForMonitor`).
- Position the `RadialWindow` centered at the cursor in that monitor's DIP space.

#### [MODIFY] [RadialDimmerOverlay.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/RadialLauncher/RadialDimmerOverlay.cs)

**`ShowDimmer()`**: Replace `SystemParameters.VirtualScreen*` with the specific monitor bounds from `MonitorFromPoint`:
```csharp
// Instead of covering all monitors, cover only the active monitor
Left = monitorDipLeft;
Top = monitorDipTop;
Width = monitorDipWidth;
Height = monitorDipHeight;
```
Pass the active monitor info from `RadialWindow.ShowAt()` to `ShowDimmer()`.

#### 4.2 — Radial Settings Toolbar Cleanup

#### [MODIFY] [MainWindow.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml)

**Lines 1183–1184**: Remove the "▶ Test Wheel" and "💾 Save Settings" buttons from the Radial Launcher header `StackPanel`.

**Lines 1194, 1767–1813**: Remove the "⚙️ Advanced" `RadioButton` tab and the `SubPanelAdvanced` StackPanel (keep its content — Precision Mode and Fullscreen Protection — merged into the Activation panel or hidden).

#### [MODIFY] [MainWindow.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml.cs)

- Remove `OnTestRadialClick` and `OnSaveRadialSettingsClick` handlers (or stub them as no-ops if referenced elsewhere).

---

### SECTION 5: Dynamic Island Refinements

#### 5.1 — Fullscreen Toggle Consolidation

#### [MODIFY] [MainWindow.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml)

**Line 1836**: Remove `ChkHideOnFullscreen` checkbox entirely.

#### [MODIFY] [MainWindow.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml.cs)

- Remove handler logic for `ChkHideOnFullscreen`.

#### 5.2 — Notification Title Consistency

#### [MODIFY] [DynamicIslandWindow.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/DynamicIsland/DynamicIslandWindow.xaml.cs)

- In `PostEvent()` / `Expand()`: ensure `ExpandedTitle.Text` is always set from `_currentEvent.Title` (not from a static placeholder).
- Review all paths that set `ExpandedTitle` and unify to use `event.Title` consistently.

#### 5.3 — Duplicate Settings Button Removal

#### [MODIFY] [DynamicIslandWindow.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/DynamicIsland/DynamicIslandWindow.xaml)

**Line 466**: Remove `BtnOpenTheHubDashboard` (the lower "⚡ The Hub — Open Settings" button from the DashboardControlsPanel). Keep only `BtnHeaderTheHub` (line 342) in the header row.

#### 5.4 — White Border Suppression

#### [MODIFY] [DynamicIslandWindow.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/DynamicIsland/DynamicIslandWindow.xaml)

Add `WindowChrome` with `GlassFrameThickness="0"`:
```xml
<WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight="0" GlassFrameThickness="0" ResizeBorderThickness="0" UseAeroCaptionButtons="False"/>
</WindowChrome.WindowChrome>
```

#### [MODIFY] [DynamicIslandWindow.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.Overlays/DynamicIsland/DynamicIslandWindow.xaml.cs)

In `OnSourceInitialized`: call `DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, 0xFFFFFFFE, ...)` to suppress DWM border.

---

### SECTION 6: Global Settings & Options Tab

#### 6.1 — Persistent Save Settings Button

#### [MODIFY] [MainWindow.xaml](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml)

**Lines 506–513** (sidebar bottom section): Add a "💾 Save Settings" button directly above the Emergency Kill Switch container:
```xml
<Button Content="💾 Save Settings" Style="{StaticResource SecondaryButtonStyle}" 
        Margin="0,0,0,8" Click="OnGlobalSaveSettingsClick" 
        ToolTip="Save all configuration to disk immediately"/>
```

#### [MODIFY] [MainWindow.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml.cs)

Add `OnGlobalSaveSettingsClick` handler that calls `SettingsManager.SaveSettings()`.

#### 6.2 — Navigation Rename (already done)

The nav button already shows "⚙️  Options" at **line 503** (`Tag="5"`). The page title at line 2008 already says "Options & Preferences". **No change needed.**

#### 6.3 — External Font Management (already partially implemented)

**Lines 2011–2052** already contain the "Custom Fonts" manager card with `FontsListControl`, `OnInstallFontClick`, `OnUseFontClick`, `OnDeleteFontClick`.

#### [MODIFY] [MainWindow.xaml.cs](file:///c:/Users/d/Desktop/the%20hub/TheHub.App/MainWindow.xaml.cs)

Verify and complete the font management handlers:
- `OnInstallFontClick`: `OpenFileDialog` for `.ttf`/`.otf`, copy to `%AppData%/TheHub/Fonts/`, register as `FontFamily` in `Application.Current.Resources["GlobalCustomFont"]`.
- `OnUseFontClick`: Set `Application.Current.Resources["GlobalCustomFont"]` to selected font, update all widget windows.
- `OnDeleteFontClick`: Remove file, refresh `FontsListControl`.
- Save selected font to `AppSettings.GlobalFontFamily`.

#### [MODIFY] [AppSettings model](file:///c:/Users/d/Desktop/the%20hub/TheHub.Common) (TheHub.Common/Models or Config)

- Add `string? GlobalFontFamily` property.
- Add `List<string> CustomFonts` property.

---

## Verification Plan

### Automated Tests
```powershell
cd "c:\Users\d\Desktop\the hub"
dotnet build TheHub.sln --configuration Debug
```
Expected: 0 errors, 0 warnings.

### Manual Verification
1. **Wallpapers**: Confirm header reads "TARGET DISPLAY" with no overlap. Toggle fullscreen/battery checkboxes and verify `AppSettings` persists.
2. **Widgets scroll**: Open Widgets tab, mouse-wheel scroll works without clicking inside.
3. **Clock widget**: Confirm no ONLINE badge, no date strip; borders tightly frame digits.
4. **Hardware widgets**: Verify CPU, GPU, RAM appear as 3 independent draggable windows.
5. **Color picker**: Confirm full HSB picker appears in Widgets controls area.
6. **Icon contrast**: Open Workspaces > icon picker popup — glyphs visible on dark cards.
7. **Radial on Display 2**: Trigger radial launcher with cursor on Display 2 — wheel centers on Display 2, dimmer covers only Display 2.
8. **Radial UI**: Confirm no "Test" or "Save Settings" buttons; no "Advanced" tab.
9. **Dynamic Island**: Confirm no "Hide on Fullscreen" checkbox. Expand island — title matches the banner. No lower settings button. No white top border.
10. **Save Settings button**: Sidebar "Save Settings" commits to JSON.
11. **Options font import**: Install a .ttf, click "Use" — clock widget updates typography.
