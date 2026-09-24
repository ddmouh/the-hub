using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Monitors;
using TheHub.Core.Native;
using TheHub.Core.Services;

namespace TheHub.Overlays.DynamicIsland;

public partial class DynamicIslandWindow : Window
{
    private const int DWMWA_BORDER_COLOR = 34;
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private readonly DispatcherTimer _collapseTimer;
    private DynamicIslandEvent? _currentEvent;
    private bool _isMouseOver;
    private bool _isExpanded;
    private bool _isInternalSliderUpdating = true;

    // Services for async COM operations
    private readonly IAudioService _audioService;
    private readonly IBrightnessService _brightnessService;

    // Drag-and-drop state
    private bool _isDragging;
    private Point _dragStartScreen;
    private double _dragStartLeft;
    private double _dragStartTop;

    // Audio Devices State
    private List<AudioDeviceInfo> _playbackDevices = new();
    private List<AudioDeviceInfo> _recordingDevices = new();
    private int _currentPlaybackIndex = 0;
    private int _currentRecordingIndex = 0;
    private enum SelectDropdownMode { None, Output, Input }
    private SelectDropdownMode _activeSelectMode = SelectDropdownMode.None;

    public event Func<Task>? PlayPauseRequested;
    public event Func<Task>? NextRequested;
    public event Func<Task>? PrevRequested;
    public event Action? OpenSettingsRequested;
    private bool _isDeafened = false;
    private int _savedDeafenVolume = 50;

    public DynamicIslandWindow()
    {
        InitializeComponent();

        // Initialize services for async COM operations
        _audioService = new AudioService();
        _brightnessService = new BrightnessService();

        _collapseTimer = new DispatcherTimer();
        _collapseTimer.Tick += (s, e) =>
        {
            _collapseTimer.Stop();
            if (!_isMouseOver && (_currentEvent == null || !_currentEvent.IsPersistent))
            {
                Collapse();
            }
        };

        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // The island draws its own rounded border; prevent DWM from adding a white frame.
        int noBorder = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));

        int exStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
        Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE,
            exStyle | Win32Api.WS_EX_NOACTIVATE | Win32Api.WS_EX_TOOLWINDOW);

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
        Win32Api.RegisterUsbDeviceNotification(hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Api.WM_DEVICECHANGE)
        {
            int changeType = wParam.ToInt32();
            if (changeType == Win32Api.DBT_DEVICEARRIVAL || changeType == Win32Api.DBT_DEVICEREMOVECOMPLETE)
            {
                bool isArrival = (changeType == Win32Api.DBT_DEVICEARRIVAL);
                if (SettingsManager.Current.IslandShowUsb)
                {
                    PostEvent(new DynamicIslandEvent
                    {
                        Type = IslandEventType.Usb,
                        Title = isArrival ? "USB Device Connected" : "USB Device Removed",
                        Subtitle = isArrival ? "New hardware connected" : "Hardware safely removed",
                        Glyph = isArrival ? "🔌" : "⏏️",
                        DisplayDurationMs = 2500
                    });
                }
                Task.Run(RefreshAudioDevices);
            }
        }

        if (msg == Win32Api.WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)Win32Api.MA_NOACTIVATE;
        }

        if (msg == Win32Api.WM_NCHITTEST)
        {
            try
            {
                int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

                Point screenPt = new Point(screenX, screenY);
                Point windowPt = PointFromScreen(screenPt);

                if (IslandContainer != null && IslandContainer.IsVisible)
                {
                    double scaleX = IslandScaleTransform?.ScaleX ?? 1.0;
                    double scaleY = IslandScaleTransform?.ScaleY ?? 1.0;

                    double w = (IslandContainer.ActualWidth > 0 ? IslandContainer.ActualWidth : IslandContainer.Width) * scaleX;
                    double h = (IslandContainer.ActualHeight > 0 ? IslandContainer.ActualHeight : IslandContainer.Height) * scaleY;

                    Point origin;
                    try
                    {
                        origin = IslandContainer.TranslatePoint(new Point(0, 0), this);
                    }
                    catch
                    {
                        origin = new Point(Math.Max(0, (ActualWidth - w) / 2.0), 0);
                    }

                    Rect bounds = new Rect(origin.X, origin.Y, w, h);
                    bounds.Inflate(8, 8);

                    if (bounds.Contains(windowPt))
                    {
                        handled = true;
                        return (IntPtr)Win32Api.HTCLIENT;
                    }
                }

                handled = true;
                return (IntPtr)Win32Api.HTTRANSPARENT;
            }
            catch
            {
                handled = true;
                return (IntPtr)Win32Api.HTTRANSPARENT;
            }
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DisplayDimmerOverlay.Instance.Initialize();
        PositionWindow();
        SetCompactState(false);
        RefreshAudioDevices();
        SyncSlidersWithSystem();

        SldVolume.ValueChanged += OnVolumeSliderChanged;
        SldBrightness.ValueChanged += OnBrightnessSliderChanged;
    }

    public void PositionWindow()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        var settings = SettingsManager.Current;
        var mon = DisplayMonitorHelper.GetTargetMonitor(settings.IslandMonitorIndex);

        double monLeft = mon.Bounds.Left / dpiX;
        double monTop = mon.Bounds.Top / dpiY;
        double monWidth = (mon.Bounds.Right - mon.Bounds.Left) / dpiX;

        double scale = Math.Clamp(settings.IslandScale, 0.6, 1.8);
        if (IslandScaleTransform != null)
        {
            IslandScaleTransform.ScaleX = scale;
            IslandScaleTransform.ScaleY = scale;
        }

        Width = Math.Max(540, 540 * scale);
        Height = Math.Max(420, 420 * scale);

        if (settings.IslandCustomX.HasValue && settings.IslandCustomY.HasValue)
        {
            Left = monLeft + settings.IslandCustomX.Value;
            Top = monTop + settings.IslandCustomY.Value;
        }
        else
        {
            Left = monLeft + (monWidth - Width) / 2.0;
            Top = monTop + settings.IslandTopOffset;
        }

        Topmost = true;
    }

    public void ResetPositionToCenter()
    {
        var settings = SettingsManager.Current;
        settings.IslandCustomX = null;
        settings.IslandCustomY = null;
        SettingsManager.SaveSettings(settings);
        PositionWindow();
    }

    public void SetAmbientStatus(AmbientStatusInfo status)
    {
        Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(status.Glyph))
                CompactGlyph.Text = status.Glyph;
            if (!string.IsNullOrWhiteSpace(status.Title))
                CompactTitle.Text = status.Title;

            if (_currentEvent == null)
            {
                ExpandedGlyph.Text = status.Glyph;
                ExpandedTitle.Text = status.Title;
                ExpandedSubtitle.Text = status.Subtitle;
            }
        });
    }

    public void PostEvent(DynamicIslandEvent evt)
    {
        Dispatcher.Invoke(() =>
        {
            _currentEvent = evt;

            ExpandedGlyph.Text = evt.Glyph;
            ExpandedTitle.Text = evt.Title;
            ExpandedSubtitle.Text = evt.Subtitle;

            if (evt.Type == IslandEventType.Media)
            {
                MediaControlsPanel.Visibility = Visibility.Visible;
                PlayPauseButton.Content = evt.IsPlaying ? "⏸" : "▶";
            }
            else
            {
                MediaControlsPanel.Visibility = Visibility.Collapsed;
            }

            // Sync sliders if this was volume or brightness
            if (evt.Type == IslandEventType.Volume && evt.Value >= 0)
            {
                _isInternalSliderUpdating = true;
                SldVolume.Value = evt.Value;
                TxtVolumePercent.Text = $"{evt.Value}%";
                BtnAudioMute.Content = evt.Value == 0 ? "🔇" : "🔊";
                _isInternalSliderUpdating = false;
            }

            Expand();

            _collapseTimer.Stop();
            if (!evt.IsPersistent)
            {
                _collapseTimer.Interval = TimeSpan.FromMilliseconds(evt.DisplayDurationMs);
                _collapseTimer.Start();
            }
        });
    }

    private double GetExpandedTargetHeight()
    {
        double baseH = (MediaControlsPanel.Visibility == Visibility.Visible) ? 255 : 215;
        if (ShadcnSelectDropdown != null && ShadcnSelectDropdown.Visibility == Visibility.Visible)
        {
            int count = SelectItemsList?.Children.Count ?? 0;
            double listH = Math.Min(130, Math.Max(32, count * 29));
            baseH += 38 + listH;
        }
        return baseH;
    }

    public void Expand()
    {
        if (_isExpanded) return;
        _isExpanded = true;

        CompactView.Visibility = Visibility.Collapsed;
        ExpandedView.Visibility = Visibility.Visible;
        if (DashboardControlsPanel != null)
            DashboardControlsPanel.Visibility = Visibility.Visible;
        if (BtnDismiss != null)
            BtnDismiss.Visibility = Visibility.Visible;

        SyncSlidersWithSystem();
        RefreshAudioDevices();

        var settings = SettingsManager.Current;
        if (QuickControlsRow != null)
            QuickControlsRow.Visibility = settings.IslandShowHoverQuickControls ? Visibility.Visible : Visibility.Collapsed;
        if (BrightnessControlGrid != null)
            BrightnessControlGrid.Visibility = settings.IslandShowBrightnessControl ? Visibility.Visible : Visibility.Collapsed;
        if (BtnMicMute != null)
            BtnMicMute.Visibility = settings.IslandShowMicMute ? Visibility.Visible : Visibility.Collapsed;

        double targetHeight = GetExpandedTargetHeight();
        AnimateContainer(180, 410, 36, targetHeight, 18, 16);
    }

    public void Collapse()
    {
        if (!_isExpanded) return;
        _isExpanded = false;

        CloseSelectDropdown(false);

        ExpandedView.Visibility = Visibility.Collapsed;
        if (DashboardControlsPanel != null)
            DashboardControlsPanel.Visibility = Visibility.Collapsed;
        if (BtnDismiss != null)
            BtnDismiss.Visibility = Visibility.Collapsed;
        CompactView.Visibility = Visibility.Visible;

        double fromH = IslandContainer.ActualHeight > 0 ? IslandContainer.ActualHeight : GetExpandedTargetHeight();
        AnimateContainer(410, 180, fromH, 36, 16, 18);
    }

    public void UpdateLayoutSettings()
    {
        var settings = SettingsManager.Current;
        if (QuickControlsRow != null)
            QuickControlsRow.Visibility = settings.IslandShowHoverQuickControls ? Visibility.Visible : Visibility.Collapsed;
        if (BrightnessControlGrid != null)
            BrightnessControlGrid.Visibility = settings.IslandShowBrightnessControl ? Visibility.Visible : Visibility.Collapsed;
        if (BtnMicMute != null)
            BtnMicMute.Visibility = settings.IslandShowMicMute ? Visibility.Visible : Visibility.Collapsed;

        if (_isExpanded)
        {
            double targetH = GetExpandedTargetHeight();
            IslandContainer.Height = targetH;
        }
    }

    public void ShowSettingsPreview()
    {
        Expand();
        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromSeconds(3);
        _collapseTimer.Start();
    }

    private void SyncSlidersWithSystem()
    {
        try
        {
            _isInternalSliderUpdating = true;

            var (vol, isMuted) = SystemControlHelper.GetMasterAudioState();
            if (vol > 0) _savedDeafenVolume = vol;
            SldVolume.Value = vol;
            TxtVolumePercent.Text = isMuted ? $"{vol}% (Muted)" : $"{vol}%";
            BtnAudioMute.Content = isMuted ? "🔇" : (vol > 65 ? "🔊" : (vol > 30 ? "🔉" : "🔈"));

            int brightness = SystemControlHelper.GetScreenBrightness();
            SldBrightness.Value = brightness;
            TxtBrightnessPercent.Text = $"{brightness}%";

            bool micMuted = SystemControlHelper.GetMicrophoneMuteState();
            UpdateMicButtonVisual(micMuted);
            UpdateDeafenButtonVisual(isMuted);
        }
        catch { }
        finally
        {
            _isInternalSliderUpdating = false;
        }
    }

    private void UpdateMicButtonVisual(bool isMuted)
    {
        if (TxtMicIcon != null)
        {
            TxtMicIcon.Text = isMuted ? "🔇" : "🎙️";
        }
        if (TxtMicLabel != null)
        {
            TxtMicLabel.Text = isMuted ? "Mic Muted" : "Mic Active";
            TxtMicLabel.Foreground = isMuted
                ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
                : new SolidColorBrush(Color.FromRgb(52, 211, 153));
        }
    }

    private void UpdateDeafenButtonVisual(bool isDeafened)
    {
        if (TxtDeafenIcon != null)
        {
            TxtDeafenIcon.Text = isDeafened ? "🔇" : "🎧";
        }
        if (TxtDeafenLabel != null)
        {
            TxtDeafenLabel.Text = isDeafened ? "Deafened" : "Deafen";
            TxtDeafenLabel.Foreground = isDeafened
                ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
                : new SolidColorBrush(Color.FromRgb(228, 228, 231));
        }
    }

    private void RefreshAudioDevices()
    {
        Task.Run(() =>
        {
            var plays = AudioDeviceManager.GetPlaybackDevices();
            var recs = AudioDeviceManager.GetRecordingDevices();

            Dispatcher.Invoke(() =>
            {
                if (plays.Count > 0) _playbackDevices = plays;
                if (recs.Count > 0) _recordingDevices = recs;

                var curPlay = _playbackDevices.FirstOrDefault(d => d.IsDefault) ?? _playbackDevices.FirstOrDefault();
                if (curPlay != null)
                {
                    _currentPlaybackIndex = _playbackDevices.IndexOf(curPlay);
                    string clean = curPlay.Name.Length > 18 ? curPlay.Name[..17] + "…" : curPlay.Name;
                    TxtOutputDevice.Text = $"🔊 {clean} ▾";
                }
                else
                {
                    TxtOutputDevice.Text = "🔊 Default Output ▾";
                }

                var curRec = _recordingDevices.FirstOrDefault(d => d.IsDefault) ?? _recordingDevices.FirstOrDefault();
                if (curRec != null)
                {
                    _currentRecordingIndex = _recordingDevices.IndexOf(curRec);
                    string clean = curRec.Name.Length > 18 ? curRec.Name[..17] + "…" : curRec.Name;
                    TxtInputDevice.Text = $"🎙️ {clean} ▾";
                }
                else
                {
                    TxtInputDevice.Text = "🎙️ Default Mic ▾";
                }
            });
        });
    }

    private void OnOutputDeviceClick(object sender, RoutedEventArgs e)
    {
        if (_activeSelectMode == SelectDropdownMode.Output && ShadcnSelectDropdown.Visibility == Visibility.Visible)
        {
            CloseSelectDropdown(true);
            return;
        }

        OpenSelectDropdown(SelectDropdownMode.Output);
    }

    private void OnInputDeviceClick(object sender, RoutedEventArgs e)
    {
        if (_activeSelectMode == SelectDropdownMode.Input && ShadcnSelectDropdown.Visibility == Visibility.Visible)
        {
            CloseSelectDropdown(true);
            return;
        }

        OpenSelectDropdown(SelectDropdownMode.Input);
    }

    private void OnCloseSelectDropdownClick(object sender, RoutedEventArgs e)
    {
        CloseSelectDropdown(true);
    }

    private async void OpenSelectDropdown(SelectDropdownMode mode)
    {
        _activeSelectMode = mode;
        SelectItemsList.Children.Clear();

        bool isOutput = mode == SelectDropdownMode.Output;
        SelectGroupLabel.Text = isOutput ? "OUTPUT DEVICES (SPEAKERS / HEADPHONES)" : "INPUT DEVICES (MICROPHONES)";

        var cached = isOutput ? _playbackDevices : _recordingDevices;
        if (cached != null && cached.Count > 0)
        {
            foreach (var dev in cached)
            {
                var itemBtn = CreateSelectItemButton(dev, isOutput);
                SelectItemsList.Children.Add(itemBtn);
            }
        }
        else
        {
            var loadingText = new TextBlock
            {
                Text = "Detecting devices…",
                Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                FontSize = 10.5,
                Margin = new Thickness(10, 8, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            SelectItemsList.Children.Add(loadingText);
        }

        ShadcnSelectDropdown.Visibility = Visibility.Visible;

        // Smoothly animate island container height to fit the dropdown
        double initialH = GetExpandedTargetHeight();
        double curW = IslandContainer.ActualWidth > 0 ? IslandContainer.ActualWidth : 410;
        double curH = IslandContainer.ActualHeight > 0 ? IslandContainer.ActualHeight : 215;
        AnimateContainer(curW, curW, curH, initialH, 16, 16);

        // Enumerate asynchronously in background
        var devices = await Task.Run(() => isOutput ? AudioDeviceManager.GetPlaybackDevices() : AudioDeviceManager.GetRecordingDevices());

        if (_activeSelectMode != mode) return; // User closed or switched while loading

        if (devices.Count > 0)
        {
            if (isOutput) _playbackDevices = devices; else _recordingDevices = devices;
            SelectItemsList.Children.Clear();
            foreach (var dev in devices)
            {
                var itemBtn = CreateSelectItemButton(dev, isOutput);
                SelectItemsList.Children.Add(itemBtn);
            }
        }
        else if (cached == null || cached.Count == 0)
        {
            SelectItemsList.Children.Clear();
            var emptyText = new TextBlock
            {
                Text = "No devices detected",
                Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122)),
                FontSize = 10.5,
                Margin = new Thickness(10, 8, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            SelectItemsList.Children.Add(emptyText);
        }

        // Recalculate target height with the items populated
        double targetH = GetExpandedTargetHeight();
        curW = IslandContainer.ActualWidth > 0 ? IslandContainer.ActualWidth : 410;
        curH = IslandContainer.ActualHeight > 0 ? IslandContainer.ActualHeight : 215;
        AnimateContainer(curW, curW, curH, targetH, 16, 16);
    }

    private void CloseSelectDropdown(bool animate)
    {
        _activeSelectMode = SelectDropdownMode.None;
        if (ShadcnSelectDropdown != null)
        {
            ShadcnSelectDropdown.Visibility = Visibility.Collapsed;
        }

        if (animate && _isExpanded)
        {
            double targetH = GetExpandedTargetHeight();
            double curW = IslandContainer.ActualWidth > 0 ? IslandContainer.ActualWidth : 410;
            double curH = IslandContainer.ActualHeight > 0 ? IslandContainer.ActualHeight : 215;
            AnimateContainer(curW, curW, curH, targetH, 16, 16);
        }
    }

    private Button CreateSelectItemButton(AudioDeviceInfo dev, bool isOutput)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("SelectItemButtonStyle"),
            ToolTip = dev.Name
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Column 0: Checkmark (✓) for active/default device
        if (dev.IsDefault)
        {
            var check = new TextBlock
            {
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 0);
            grid.Children.Add(check);
        }

        // Column 1: Icon (🔊 or 🎧 or 🎙️)
        string icon = isOutput
            ? (dev.Name.Contains("Headphone", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ? "🎧" : "🔊")
            : "🎙️";
        var iconBlock = new TextBlock
        {
            Text = icon,
            FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(iconBlock, 1);
        grid.Children.Add(iconBlock);

        // Column 2: Name
        var nameBlock = new TextBlock
        {
            Text = dev.Name,
            FontSize = 11,
            Foreground = dev.IsDefault ? new SolidColorBrush(Color.FromRgb(96, 165, 250)) : new SolidColorBrush(Color.FromRgb(228, 228, 231)),
            FontWeight = dev.IsDefault ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        Grid.SetColumn(nameBlock, 2);
        grid.Children.Add(nameBlock);

        btn.Content = grid;

        btn.Click += (s, e) =>
        {
            AudioDeviceManager.SetDefaultDevice(dev.Id);

            string clean = dev.Name.Length > 18 ? dev.Name[..17] + "…" : dev.Name;
            if (isOutput)
            {
                TxtOutputDevice.Text = $"🔊 {clean} ▾";
            }
            else
            {
                TxtInputDevice.Text = $"🎙️ {clean} ▾";
            }

            CloseSelectDropdown(true);

            PostEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.SystemAlert,
                Title = isOutput ? "Audio Output Changed" : "Microphone Changed",
                Subtitle = dev.Name,
                Glyph = icon,
                DisplayDurationMs = 2200
            });

            Task.Run(RefreshAudioDevices);
        };

        return btn;
    }

    private async void OnDeafenClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _isDeafened = !_isDeafened;
        await _audioService.SetDeafenAsync(_isDeafened);

        UpdateDeafenButtonVisual(_isDeafened);

        var (vol, _) = SystemControlHelper.GetMasterAudioState();
        if (vol > 0 && _isDeafened)
        {
            _savedDeafenVolume = vol;
        }
        else if (!_isDeafened && (vol == 0 || _savedDeafenVolume > vol))
        {
            int restoreVol = _savedDeafenVolume > 0 ? _savedDeafenVolume : 50;
            await _audioService.SetMasterVolumeAsync(restoreVol);
            vol = restoreVol;
        }

        BtnAudioMute.Content = _isDeafened ? "🔇" : (vol > 65 ? "🔊" : (vol > 30 ? "🔉" : "🔈"));

        _isInternalSliderUpdating = true;
        SldVolume.Value = vol;
        TxtVolumePercent.Text = _isDeafened ? $"{vol}% (Muted)" : $"{vol}%";
        _isInternalSliderUpdating = false;

        PostEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.Volume,
            Title = _isDeafened ? "Audio Deafened" : "Audio Undeafened",
            Subtitle = _isDeafened ? "Speakers / audio muted" : $"{vol}% (Restored)",
            Glyph = _isDeafened ? "🔇" : (vol > 65 ? "🔊" : (vol > 30 ? "🔉" : "🔈")),
            Value = vol,
            DisplayDurationMs = 2000
        });
    }

    private async void OnVolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInternalSliderUpdating || SldVolume == null || TxtVolumePercent == null || BtnAudioMute == null) return;

        int vol = (int)Math.Round(e.NewValue);
        if (vol > 0) _savedDeafenVolume = vol;
        TxtVolumePercent.Text = $"{vol}%";
        BtnAudioMute.Content = vol == 0 ? "🔇" : (vol > 65 ? "🔊" : (vol > 30 ? "🔉" : "🔈"));

        await _audioService.SetMasterVolumeAsync(vol);
    }

    private async void OnBrightnessSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInternalSliderUpdating || SldBrightness == null || TxtBrightnessPercent == null) return;

        int b = (int)Math.Round(e.NewValue);
        TxtBrightnessPercent.Text = $"{b}%";

        var settings = SettingsManager.Current;
        var mon = DisplayMonitorHelper.GetTargetMonitor(settings.IslandMonitorIndex);
        DisplayDimmerOverlay.Instance.SetBrightness(b, mon, this);
        await _brightnessService.SetBrightnessAsync(b);
    }

    private void AnimateContainer(double fromW, double toW, double fromH, double toH, double fromR, double toR)
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var animW = new DoubleAnimation(fromW, toW, duration) { EasingFunction = ease };
        var animH = new DoubleAnimation(fromH, toH, duration) { EasingFunction = ease };

        IslandContainer.BeginAnimation(WidthProperty, animW);
        IslandContainer.BeginAnimation(HeightProperty, animH);
        IslandContainer.CornerRadius = new CornerRadius(fromR);
    }

    private void SetCompactState(bool animated)
    {
        _isExpanded = false;
        ExpandedView.Visibility = Visibility.Collapsed;
        CompactView.Visibility = Visibility.Visible;

        IslandContainer.Width = 180;
        IslandContainer.Height = 36;
        IslandContainer.CornerRadius = new CornerRadius(18);
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        // Guard: only expand if cursor is genuinely over the island pill.
        // With AllowsTransparency=True, WPF can fire MouseEnter on the Border
        // even when the cursor is near the transparent window boundary while
        // the user scrolls web pages or videos in background applications.
        var pos = e.GetPosition(IslandContainer);
        if (pos.X < -4 || pos.Y < -4 ||
            pos.X > IslandContainer.ActualWidth + 4 ||
            pos.Y > IslandContainer.ActualHeight + 4)
            return;

        _isMouseOver = true;
        Expand();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOver = false;
        if (_currentEvent == null || !_currentEvent.IsPersistent)
        {
            Collapse();
        }
    }

    #region Drag & Drop Free Positioning

    private void OnIslandMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SettingsManager.Current.IslandLockPosition) return;
        if (IsClickOnInteractiveControl(e.OriginalSource as DependencyObject)) return;

        _isDragging = true;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        IslandContainer.CaptureMouse();
    }

    private void OnIslandMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            Point currentScreen = PointToScreen(e.GetPosition(this));
            Left = _dragStartLeft + (currentScreen.X - _dragStartScreen.X);
            Top = _dragStartTop + (currentScreen.Y - _dragStartScreen.Y);
        }
    }

    private void OnIslandMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            IslandContainer.ReleaseMouseCapture();

            var dpi = VisualTreeHelper.GetDpi(this);
            double dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            var settings = SettingsManager.Current;
            var mon = DisplayMonitorHelper.GetTargetMonitor(settings.IslandMonitorIndex);
            double monLeft = mon.Bounds.Left / dpiX;
            double monTop = mon.Bounds.Top / dpiY;

            settings.IslandCustomX = Left - monLeft;
            settings.IslandCustomY = Top - monTop;
            SettingsManager.SaveSettings(settings);
        }
    }

    private static bool IsClickOnInteractiveControl(DependencyObject? target)
    {
        while (target != null)
        {
            if (target is System.Windows.Controls.Button || target is System.Windows.Controls.Slider) return true;
            target = VisualTreeHelper.GetParent(target);
        }
        return false;
    }

    #endregion

    #region Quick Actions

    private async void OnVolDownClick(object sender, RoutedEventArgs e)
        {
            // Get current volume state
            var (currentVol, isMuted) = await _audioService.GetMasterVolumeStateAsync();

            // Calculate new volume (delta is -0.05f = -5%)
            int newVol = Math.Clamp(currentVol - 5, 0, 100);

            // Set the new volume
            await _audioService.SetMasterVolumeAsync(newVol);

            // Update UI
            _isInternalSliderUpdating = true;
            SldVolume.Value = isMuted ? 0 : newVol;
            TxtVolumePercent.Text = $"{newVol}%";
            BtnAudioMute.Content = isMuted ? "🔇" : (newVol > 65 ? "🔊" : (newVol > 30 ? "🔉" : "🔈"));
            _isInternalSliderUpdating = false;

            PostEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.Volume,
                Title = "Master Volume",
                Subtitle = $"{newVol}%",
                Glyph = isMuted ? "🔇" : "🔉",
                Value = newVol,
                DisplayDurationMs = 1800
            });
        }

    private async void OnVolUpClick(object sender, RoutedEventArgs e)
        {
            // Get current volume state
            var (currentVol, isMuted) = await _audioService.GetMasterVolumeStateAsync();

            // Calculate new volume (delta is +0.05f = +5%)
            int newVol = Math.Clamp(currentVol + 5, 0, 100);

            // Set the new volume
            await _audioService.SetMasterVolumeAsync(newVol);

            // Update UI
            _isInternalSliderUpdating = true;
            SldVolume.Value = isMuted ? 0 : newVol;
            TxtVolumePercent.Text = $"{newVol}%";
            BtnAudioMute.Content = isMuted ? "🔇" : (newVol > 65 ? "🔊" : (newVol > 30 ? "🔉" : "🔈"));
            _isInternalSliderUpdating = false;

            PostEvent(new DynamicIslandEvent
            {
                Type = IslandEventType.Volume,
                Title = "Master Volume",
                Subtitle = $"{newVol}%",
                Glyph = isMuted ? "🔇" : "🔊",
                Value = newVol,
                DisplayDurationMs = 1800
            });
        }

    private async void OnAudioMuteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var (vol, isMuted) = await _audioService.ToggleMasterMuteAsync();
        BtnAudioMute.Content = isMuted ? "🔇" : (vol > 65 ? "🔊" : (vol > 30 ? "🔉" : "🔈"));

        if (isMuted && vol > 0)
        {
            _savedDeafenVolume = vol;
        }
        else if (!isMuted && (vol == 0 || _savedDeafenVolume > vol))
        {
            int restoreVol = _savedDeafenVolume > 0 ? _savedDeafenVolume : 50;
            await _audioService.SetMasterVolumeAsync(restoreVol);
            vol = restoreVol;
        }

        _isInternalSliderUpdating = true;
        SldVolume.Value = vol;
        TxtVolumePercent.Text = isMuted ? $"{vol}% (Muted)" : $"{vol}%";
        _isInternalSliderUpdating = false;

        PostEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.Volume,
            Title = isMuted ? "Audio Muted" : "Audio Unmuted",
            Subtitle = isMuted ? "Speakers muted" : $"{vol}%",
            Glyph = isMuted ? "🔇" : (vol > 65 ? "🔊" : (vol > 30 ? "🔉" : "🔈")),
            Value = vol,
            DisplayDurationMs = 2000
        });
    }

    private async void OnMicMuteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        bool isMuted = await _audioService.ToggleMicrophoneMuteAsync();
        UpdateMicButtonVisual(isMuted);

        PostEvent(new DynamicIslandEvent
        {
            Type = IslandEventType.SystemAlert,
            Title = isMuted ? "Microphone Muted" : "Microphone Active",
            Subtitle = isMuted ? "Voice input muted" : "Voice input live",
            Glyph = isMuted ? "🔇" : "🎙️",
            DisplayDurationMs = 2000
        });
    }

    #endregion

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        _currentEvent = null;
        Collapse();
    }

    private void OnOpenTheHubClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Collapse();
        OpenSettingsRequested?.Invoke();
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (PlayPauseRequested != null)
        {
            await PlayPauseRequested.Invoke();
        }
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (NextRequested != null)
        {
            await NextRequested.Invoke();
        }
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (PrevRequested != null)
        {
            await PrevRequested.Invoke();
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  WORKSPACES QUICK LAUNCHER
    // ══════════════════════════════════════════════════════════════════════════

    private List<WorkspaceConfig> _workspaces = new();
    private bool _workspacePopoverOpen = false;

    /// <summary>
    /// Called by MainWindow whenever the workspace list changes.
    /// Rebuilds the popover rows and shows/hides the trigger row accordingly.
    /// </summary>
    public void SetWorkspaces(IEnumerable<WorkspaceConfig> workspaces)
    {
        _workspaces = workspaces?.ToList() ?? new List<WorkspaceConfig>();
        Dispatcher.BeginInvoke(() =>
        {
            BuildWorkspacePopoverItems();
            // Hide trigger row when no workspaces are configured
            if (WorkspaceTriggerRow != null)
                WorkspaceTriggerRow.Visibility = _workspaces.Count > 0
                    ? Visibility.Visible : Visibility.Collapsed;
        });
    }


    private void BuildWorkspacePopoverItems()
    {
        if (WorkspacePopoverList == null) return;
        WorkspacePopoverList.Children.Clear();

        foreach (var ws in _workspaces)
        {
            // Row border
            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2E)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Accent color indicator dot
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            try
            {
                var dotColor = (Color)ColorConverter.ConvertFromString(ws.AccentColor ?? "#3B82F6");
                dot.Fill = new SolidColorBrush(dotColor);
            }
            catch
            {
                dot.Fill = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
            }
            Grid.SetColumn(dot, 0);

            // Workspace name
            var nameBlock = new TextBlock
            {
                Text = $"{ws.IconGlyph} {ws.Name}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameBlock, 1);

            // ▶ Run button
            var runBtn = new Button
            {
                Content = "▶ Run",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 8, 3),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = ws
            };
            runBtn.Click += OnWorkspaceRunClick;
            Grid.SetColumn(runBtn, 2);

            grid.Children.Add(dot);
            grid.Children.Add(nameBlock);
            grid.Children.Add(runBtn);
            row.Child = grid;
            WorkspacePopoverList.Children.Add(row);
        }

        // Empty state
        if (_workspaces.Count == 0)
        {
            WorkspacePopoverList.Children.Add(new TextBlock
            {
                Text = "No workspaces configured yet.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A)),
                Margin = new Thickness(4, 4, 4, 4)
            });
        }
    }

    private void OnWorkspaceTriggerClick(object sender, RoutedEventArgs e)
    {
        _workspacePopoverOpen = !_workspacePopoverOpen;
        WorkspacePopover.Visibility = _workspacePopoverOpen ? Visibility.Visible : Visibility.Collapsed;

        if (_workspacePopoverOpen)
        {
            // Animate fade-in
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            WorkspacePopover.BeginAnimation(UIElement.OpacityProperty, anim);
            TxtWorkspaceChevron.Text = " ▴";

            // Grow island height so the popover list is fully visible
            int wsCount = Math.Max(1, _workspaces.Count);
            double popoverH = 36 + Math.Min(wsCount, 6) * 36.0;
            double curWp = IslandContainer.ActualWidth > 0 ? IslandContainer.ActualWidth : 410;
            double curHp = IslandContainer.ActualHeight > 0 ? IslandContainer.ActualHeight : GetExpandedTargetHeight();
            AnimateContainer(curWp, curWp, curHp, curHp + popoverH, 16, 16);
        }
        else
        {
            TxtWorkspaceChevron.Text = " ▾";
            // Shrink back to normal expanded height
            double curWc = IslandContainer.ActualWidth > 0 ? IslandContainer.ActualWidth : 410;
            double curHc = IslandContainer.ActualHeight > 0 ? IslandContainer.ActualHeight : GetExpandedTargetHeight();
            AnimateContainer(curWc, curWc, curHc, GetExpandedTargetHeight(), 16, 16);
        }
    }

    private void OnCloseWorkspacePopoverClick(object sender, RoutedEventArgs e)
    {
        _workspacePopoverOpen = false;
        WorkspacePopover.Visibility = Visibility.Collapsed;
        TxtWorkspaceChevron.Text = " ▾";
    }

    private void OnWorkspaceRunClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WorkspaceConfig workspace)
        {
            // Close the popover immediately for clean UX
            OnCloseWorkspacePopoverClick(sender, e);

            // Fire workspace application launch off the UI thread
            Task.Run(async () => await AppLauncherService.ApplyWorkspaceAsync(workspace));
        }
    }
}
