using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using TheHub.Common.Config;

namespace TheHub.Overlays.Widgets;

public partial class DesktopClockWidget : DesktopWidgetBase
{
    private readonly DispatcherTimer _timer;
    private bool _is24Hour = true;
    private bool _showSeconds = true;
    private bool _isAnalogMode = false;
    private string _currentStyle = "Cyber Neon";
    private string _currentHexColor = "#00F0FF";
    private double _backgroundOpacity = 85.0;
    private string _currentFontFamily = "Consolas";
    private bool _hasSavedPosition;
    public event EventHandler? PositionChanged;

    public DesktopClockWidget()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += (s, e) => UpdateClock();

        MouseEnter += (s, e) => { HeaderRow.Opacity = 1.0; };
        MouseLeave += (s, e) => { HeaderRow.Opacity = 0.0; };

        Loaded += (s, e) =>
        {
            UpdateClock();
            _timer.Start();
            PinToDesktopBottom();

            // Default positioning: top-right area of primary desktop.
            // Deferred until layout resolves SizeToContent (Width/Height are NaN).
            if (!_hasSavedPosition && Left == 0 && Top == 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    double w = double.IsNaN(Width) ? ActualWidth : Width;
                    double h = double.IsNaN(Height) ? ActualHeight : Height;
                    if (w <= 0) w = 300;
                    if (h <= 0) h = 170;
                    var workArea = SystemParameters.WorkArea;
                    Left = workArea.Right - w - 12;
                    Top = workArea.Top + 60;
                    PositionChanged?.Invoke(this, EventArgs.Empty);
                }));
            }
        };

        LocationChanged += (s, e) =>
        {
            if (IsVisible) PositionChanged?.Invoke(this, EventArgs.Empty);
        };

        Unloaded += (s, e) =>
        {
            _timer.Stop();
        };
    }

    public void ApplyConfig(WidgetsConfig config)
    {
        if (config == null) return;

        _isAnalogMode = string.Equals(config.DisplayMode, "Analog", StringComparison.OrdinalIgnoreCase);
        _is24Hour = config.Is24Hour;
        _showSeconds = config.ShowSeconds;
        _currentStyle = config.VisualStyle ?? "Cyber Neon";
        _currentHexColor = string.IsNullOrWhiteSpace(config.HexColor) ? "#00F0FF" : config.HexColor;
        _backgroundOpacity = config.BackgroundOpacity;
        _currentFontFamily = string.IsNullOrWhiteSpace(config.FontFamily) ? "Consolas" : config.FontFamily;

        if (config.ClockLeft.HasValue && config.ClockTop.HasValue)
        {
            _hasSavedPosition = true;
            Left = config.ClockLeft.Value;
            Top = config.ClockTop.Value;
        }

        UpdateModeUI();
        ApplyVisualStyleAndTheme();
        UpdateClock();
    }

    private void UpdateModeUI()
    {
        if (_isAnalogMode)
        {
            DigitalPanel.Visibility = Visibility.Collapsed;
            AnalogPanel.Visibility = Visibility.Visible;
            BtnModeToggle.Content = "Digital";
            BtnFormat.Visibility = Visibility.Collapsed;
            TxtHeaderLabel.Text = "ANALOG CLOCK";
        }
        else
        {
            DigitalPanel.Visibility = Visibility.Visible;
            AnalogPanel.Visibility = Visibility.Collapsed;
            BtnModeToggle.Content = "Analog";
            BtnFormat.Visibility = Visibility.Visible;
            BtnFormat.Content = _is24Hour ? "24H" : "12H";
            TxtHeaderLabel.Text = "DIGITAL CLOCK";
        }

        TxtTimeSeconds.Visibility = _showSeconds ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetDisplayMode(string mode)
    {
        _isAnalogMode = string.Equals(mode, "Analog", StringComparison.OrdinalIgnoreCase);
        UpdateModeUI();
    }

    public void SetTimeFormat(bool is24Hour, bool showSeconds, bool showDate)
    {
        _is24Hour = is24Hour;
        _showSeconds = showSeconds;
        UpdateModeUI();
        ApplyVisualStyleAndTheme();
        UpdateClock();
    }

    public void SetVisualStyle(string style)
    {
        _currentStyle = style;
        ApplyVisualStyleAndTheme();
    }

    public void SetGlowColor(string hexColor)
    {
        _currentHexColor = hexColor;
        ApplyVisualStyleAndTheme();
    }

    public void SetBackgroundOpacity(double opacityPercent)
    {
        _backgroundOpacity = Math.Clamp(opacityPercent, 0.0, 100.0);
        ApplyVisualStyleAndTheme();
    }

    public void SetClockFont(string fontName)
    {
        _currentFontFamily = fontName;
        ApplyVisualStyleAndTheme();
    }

    private void ApplyVisualStyleAndTheme()
    {
        try
        {
            var brush = (Brush)new BrushConverter().ConvertFrom(_currentHexColor)!;
            var solidColor = ((SolidColorBrush)brush).Color;

            // Selected font (system or user-installed custom) applies to every
            // preset so style switching and font selection compose and re-render.
            var selectedFont = CustomFontService.ResolveFontFamily(_currentFontFamily);

            double opacityRatio = Math.Clamp(_backgroundOpacity / 100.0, 0.0, 1.0);
            byte alpha = (byte)Math.Round(opacityRatio * 255.0);
            bool isFrameless = _backgroundOpacity <= 0.01;

            switch (_currentStyle)
            {
                case "Clean Minimalist":
                    HoursGlow.Opacity = 0.0;
                    HoursGlow.BlurRadius = 0;
                    SecondsGlow.Opacity = 0.0;
                    SecondsGlow.BlurRadius = 0;
                    TxtTimeAmPm.Effect = null;

                    TxtTimeHoursMinutes.FontFamily = selectedFont;
                    TxtTimeHoursMinutes.FontWeight = FontWeights.Normal;
                    TxtTimeSeconds.FontFamily = selectedFont;

                    TxtTimeHoursMinutes.Foreground = Brushes.White;
                    TxtTimeSeconds.Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231));
                    TxtTimeAmPm.Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231));
                    HourHand.Stroke = Brushes.White;
                    MinuteHand.Stroke = new SolidColorBrush(Color.FromRgb(228, 228, 231));
                    SecondHand.Stroke = new SolidColorBrush(Color.FromRgb(161, 161, 170));
                    AnalogCenterPivot.Fill = Brushes.White;

                    if (isFrameless)
                    {
                        MainBorder.Background = Brushes.Transparent;
                        MainBorder.BorderThickness = new Thickness(0);
                        MainBorder.BorderBrush = Brushes.Transparent;
                        CardShadowEffect.Opacity = 0.0;
                    }
                    else
                    {
                        MainBorder.BorderThickness = new Thickness(0);
                        MainBorder.BorderBrush = Brushes.Transparent;
                        MainBorder.Background = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.35), 18, 18, 22));
                        CardShadowEffect.Opacity = 0.0;
                    }
                    break;

                case "Glassmorphism":
                    TxtTimeHoursMinutes.FontFamily = selectedFont;
                    TxtTimeHoursMinutes.FontWeight = FontWeights.SemiBold;
                    TxtTimeSeconds.FontFamily = selectedFont;

                    TxtTimeHoursMinutes.Foreground = new SolidColorBrush(Color.FromRgb(240, 245, 255));
                    TxtTimeSeconds.Foreground = new SolidColorBrush(Color.FromRgb(200, 215, 235));
                    TxtTimeAmPm.Foreground = new SolidColorBrush(Color.FromRgb(200, 215, 235));
                    TxtTimeAmPm.Effect = null;

                    HourHand.Stroke = new SolidColorBrush(Color.FromRgb(240, 245, 255));
                    MinuteHand.Stroke = new SolidColorBrush(Color.FromRgb(200, 215, 235));
                    SecondHand.Stroke = brush;
                    AnalogCenterPivot.Fill = brush;

                    HoursGlow.Opacity = 0.4;
                    HoursGlow.BlurRadius = 14;
                    HoursGlow.Color = Colors.White;
                    SecondsGlow.Opacity = 0.3;
                    SecondsGlow.BlurRadius = 10;
                    SecondsGlow.Color = Colors.White;

                    if (isFrameless)
                    {
                        MainBorder.Background = Brushes.Transparent;
                        MainBorder.BorderThickness = new Thickness(0);
                        MainBorder.BorderBrush = Brushes.Transparent;
                        CardShadowEffect.Opacity = 0.0;
                    }
                    else
                    {
                        MainBorder.BorderThickness = new Thickness(1.0);
                        MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(opacityRatio * 90), 160, 180, 220));
                        MainBorder.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Min((int)alpha, 60), 0, 0, 0));
                        CardShadowEffect.Opacity = opacityRatio * 0.6;
                    }
                    break;

                case "Retro Digital":
                    TxtTimeHoursMinutes.FontFamily = selectedFont;
                    TxtTimeHoursMinutes.FontWeight = FontWeights.Bold;
                    TxtTimeSeconds.FontFamily = selectedFont;

                    TxtTimeHoursMinutes.Foreground = brush;
                    TxtTimeSeconds.Foreground = brush;
                    TxtTimeAmPm.Foreground = brush;
                    TxtTimeAmPm.Effect = new DropShadowEffect { BlurRadius = 6, Color = solidColor, Opacity = 0.5, ShadowDepth = 0 };

                    HourHand.Stroke = brush;
                    MinuteHand.Stroke = brush;
                    SecondHand.Stroke = brush;
                    AnalogCenterPivot.Fill = brush;

                    HoursGlow.Opacity = 0.55;
                    HoursGlow.BlurRadius = 8;
                    HoursGlow.Color = solidColor;
                    SecondsGlow.Opacity = 0.45;
                    SecondsGlow.BlurRadius = 6;
                    SecondsGlow.Color = solidColor;

                    if (isFrameless)
                    {
                        MainBorder.Background = Brushes.Transparent;
                        MainBorder.BorderThickness = new Thickness(0);
                        MainBorder.BorderBrush = Brushes.Transparent;
                        CardShadowEffect.Opacity = 0.0;
                    }
                    else
                    {
                        MainBorder.BorderThickness = new Thickness(1.5);
                        MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(opacityRatio * 180), 30, 40, 35));
                        MainBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 8, 12, 10));
                        CardShadowEffect.Opacity = opacityRatio * 0.5;
                    }
                    break;

                case "Cyber Neon":
                default:
                    TxtTimeHoursMinutes.FontFamily = selectedFont;
                    TxtTimeHoursMinutes.FontWeight = FontWeights.Bold;
                    TxtTimeSeconds.FontFamily = selectedFont;

                    TxtTimeHoursMinutes.Foreground = brush;
                    TxtTimeSeconds.Foreground = brush;
                    TxtTimeAmPm.Foreground = brush;
                    TxtTimeAmPm.Effect = new DropShadowEffect { BlurRadius = 10, Color = solidColor, Opacity = 0.85, ShadowDepth = 0 };

                    HourHand.Stroke = Brushes.White;
                    MinuteHand.Stroke = new SolidColorBrush(Color.FromRgb(212, 212, 216));
                    SecondHand.Stroke = brush;
                    AnalogCenterPivot.Fill = brush;

                    HoursGlow.Opacity = 0.85;
                    HoursGlow.BlurRadius = 18;
                    HoursGlow.Color = solidColor;
                    SecondsGlow.Opacity = 0.75;
                    SecondsGlow.BlurRadius = 12;
                    SecondsGlow.Color = solidColor;

                    if (isFrameless)
                    {
                        MainBorder.Background = Brushes.Transparent;
                        MainBorder.BorderThickness = new Thickness(0);
                        MainBorder.BorderBrush = Brushes.Transparent;
                        CardShadowEffect.Opacity = 0.0;
                    }
                    else
                    {
                        MainBorder.BorderThickness = new Thickness(1.2);
                        MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(opacityRatio * 220), solidColor.R, solidColor.G, solidColor.B));
                        MainBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 14, 15, 22));
                        CardShadowEffect.Opacity = opacityRatio * 0.6;
                    }
                    break;
            }

            // Status strip removed — no TxtStatus/FooterRow references needed
        }
        catch { }
    }

    public void UpdateClock()
    {
        var now = DateTime.Now;

        // 1. Digital Clock Update
        if (_is24Hour)
        {
            TxtTimeHoursMinutes.Text = now.ToString("HH:mm");
            TxtTimeSeconds.Text = ":" + now.ToString("ss");
            TxtTimeAmPm.Visibility = Visibility.Collapsed;
        }
        else
        {
            TxtTimeHoursMinutes.Text = now.ToString("hh:mm");
            TxtTimeSeconds.Text = ":" + now.ToString("ss");
            TxtTimeAmPm.Text = now.ToString("tt", CultureInfo.InvariantCulture).ToUpperInvariant();
            TxtTimeAmPm.Visibility = Visibility.Visible;
        }

        // Date display removed from clock widget

        // 2. Analog Clock Update
        if (_isAnalogMode)
        {
            double hourAngle = (now.Hour % 12 + now.Minute / 60.0 + now.Second / 3600.0) * 30.0;
            double minuteAngle = (now.Minute + now.Second / 60.0) * 6.0;
            double secondAngle = now.Second * 6.0;

            HourHandTransform.Angle = hourAngle;
            MinuteHandTransform.Angle = minuteAngle;
            SecondHandTransform.Angle = secondAngle;
        }
    }

    private void OnModeToggleClick(object sender, RoutedEventArgs e)
    {
        _isAnalogMode = !_isAnalogMode;
        UpdateModeUI();
    }

    private void OnFormatClick(object sender, RoutedEventArgs e)
    {
        _is24Hour = !_is24Hour;
        BtnFormat.Content = _is24Hour ? "24H" : "12H";
        UpdateClock();
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        Topmost = false;
        PinToDesktopBottom();
        BtnPin.Foreground = Brushes.Cyan;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        CloseWidget();
    }
}
