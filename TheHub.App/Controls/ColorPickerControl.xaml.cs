using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace TheHub.App.Controls;

/// <summary>
/// Full-spectrum HSB color picker with hue slider, saturation/brightness canvas, and hex input.
/// Exposes SelectedColor dependency property and ColorChanged event.
/// </summary>
public partial class ColorPickerControl : UserControl
{
    // ── Dependency Property ───────────────────────────────────────────────────

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorPickerControl),
            new PropertyMetadata(Color.FromRgb(0, 240, 255), OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public event Action<Color>? ColorChanged;

    // ── Internal state ────────────────────────────────────────────────────────

    private double _hue = 180.0;     // 0..360
    private double _saturation = 1.0; // 0..1
    private double _brightness = 1.0; // 0..1

    private bool _isDraggingSB;
    private bool _isDraggingHue;
    private bool _suppressUpdate;

    public ColorPickerControl()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            UpdateFromHSB();
            RefreshHueCursor();
            RefreshSBCursor();
        };
    }

    // ── External property change ──────────────────────────────────────────────

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorPickerControl ctrl && !ctrl._suppressUpdate)
        {
            ctrl.SetFromColor((Color)e.NewValue);
        }
    }

    private void SetFromColor(Color c)
    {
        ColorToHsb(c, out _hue, out _saturation, out _brightness);
        UpdateFromHSB();
        RefreshHueCursor();
        RefreshSBCursor();
    }

    // ── SB canvas mouse events ─────────────────────────────────────────────────

    private void OnSBMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSB = true;
        (sender as UIElement)!.CaptureMouse();
        UpdateSBFromPoint(e.GetPosition(SBCanvas));
    }

    private void OnSBMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSB) UpdateSBFromPoint(e.GetPosition(SBCanvas));
    }

    private void OnSBMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSB = false;
        (sender as UIElement)!.ReleaseMouseCapture();
    }

    private void UpdateSBFromPoint(Point pt)
    {
        double w = SBCanvas.ActualWidth;
        double h = SBCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        _saturation = Math.Clamp(pt.X / w, 0, 1);
        _brightness = Math.Clamp(1.0 - pt.Y / h, 0, 1);

        RefreshSBCursor();
        CommitHSB();
    }

    // ── Hue slider mouse events ────────────────────────────────────────────────

    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = true;
        (sender as UIElement)!.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition((IInputElement)sender));
    }

    private void OnHueMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingHue) UpdateHueFromPoint(e.GetPosition((IInputElement)sender));
    }

    private void OnHueMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = false;
        (sender as UIElement)!.ReleaseMouseCapture();
    }

    private void UpdateHueFromPoint(Point pt)
    {
        var track = HueCursorCanvas.Parent as FrameworkElement;
        if (track == null) return;
        double w = track.ActualWidth;
        if (w <= 0) return;

        _hue = Math.Clamp(pt.X / w * 360.0, 0.0, 360.0);
        RefreshHueCursor();
        UpdateSBBaseColor();
        CommitHSB();
    }

    // ── Hex input ─────────────────────────────────────────────────────────────

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtHex == null) return;
        if (_suppressUpdate) return;
        if (TxtHex.Text.Length == 6)
        {
            try
            {
                var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + TxtHex.Text)!;
                ColorToHsb(c, out _hue, out _saturation, out _brightness);
                RefreshHueCursor();
                RefreshSBCursor();
                UpdateFromHSB(skipHexUpdate: true);
                try { ColorChanged?.Invoke(c); } catch { }
            }
            catch { }
        }
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e)
    {
        if (TxtHex.Text.Length < 6) UpdateHexDisplay(SelectedColor);
    }

    private void OnRgbTextChanged(object sender, TextChangedEventArgs e)
    {
        // XAML can raise TextChanged while InitializeComponent is still wiring fields.
        if (TxtR == null || TxtG == null || TxtB == null) return;
        if (_suppressUpdate) return;
        if (byte.TryParse(TxtR.Text, out byte r) && byte.TryParse(TxtG.Text, out byte g) && byte.TryParse(TxtB.Text, out byte b))
        {
            var c = Color.FromRgb(r, g, b);
            ColorToHsb(c, out _hue, out _saturation, out _brightness);
            _suppressUpdate = true;
            SelectedColor = c;
            _suppressUpdate = false;
            UpdateSBBaseColor();
            ColorPreview.Background = new SolidColorBrush(c);
            TxtHex.Text = $"{r:X2}{g:X2}{b:X2}";
            RefreshHueCursor();
            RefreshSBCursor();
            try { ColorChanged?.Invoke(c); } catch { }
        }
    }

    private void OnRgbLostFocus(object sender, RoutedEventArgs e)
    {
        UpdateRgbDisplay(SelectedColor);
    }

    // ── HSB → Color helpers ───────────────────────────────────────────────────

    private void CommitHSB()
    {
        _suppressUpdate = true;
        SelectedColor = HsbToColor(_hue, _saturation, _brightness);
        _suppressUpdate = false;
        UpdateHexDisplay(SelectedColor);
        ColorPreview.Background = new SolidColorBrush(SelectedColor);
        try { ColorChanged?.Invoke(SelectedColor); } catch { }
    }

    private void UpdateFromHSB(bool skipHexUpdate = false)
    {
        UpdateSBBaseColor();
        var c = HsbToColor(_hue, _saturation, _brightness);
        ColorPreview.Background = new SolidColorBrush(c);
        if (!skipHexUpdate) UpdateHexDisplay(c);
        RefreshSBCursor();
        RefreshHueCursor();
    }

    private void UpdateSBBaseColor()
    {
        var hueColor = HsbToColor(_hue, 1.0, 1.0);
        HueBase.Background = new SolidColorBrush(hueColor);
    }

    private void RefreshSBCursor()
    {
        if (SBCursorCanvas.ActualWidth <= 0) return;
        double x = _saturation * SBCanvas.ActualWidth - 6;
        double y = (1.0 - _brightness) * SBCanvas.ActualHeight - 6;
        Canvas.SetLeft(SBCursor, Math.Clamp(x, -6, SBCanvas.ActualWidth - 6));
        Canvas.SetTop(SBCursor, Math.Clamp(y, -6, SBCanvas.ActualHeight - 6));
    }

    private void RefreshHueCursor()
    {
        if (HueCursorCanvas.ActualWidth <= 0) return;
        double track = HueCursorCanvas.ActualWidth;
        double x = (_hue / 360.0) * track - 3;
        Canvas.SetLeft(HueCursor, Math.Clamp(x, -3, track - 3));
    }

    private void UpdateHexDisplay(Color c)
    {
        _suppressUpdate = true;
        TxtHex.Text = $"{c.R:X2}{c.G:X2}{c.B:X2}";
        TxtR.Text = c.R.ToString();
        TxtG.Text = c.G.ToString();
        TxtB.Text = c.B.ToString();
        _suppressUpdate = false;
    }

    private void UpdateRgbDisplay(Color c)
    {
        _suppressUpdate = true;
        TxtR.Text = c.R.ToString();
        TxtG.Text = c.G.ToString();
        TxtB.Text = c.B.ToString();
        _suppressUpdate = false;
    }

    // ── Static Color math ─────────────────────────────────────────────────────

    private static Color HsbToColor(double h, double s, double b)
    {
        if (s == 0) { byte g = (byte)(b * 255); return Color.FromRgb(g, g, g); }
        h /= 60.0;
        int i = (int)Math.Floor(h) % 6;
        double f = h - Math.Floor(h);
        double p = b * (1 - s);
        double q = b * (1 - s * f);
        double t = b * (1 - s * (1 - f));
        return i switch
        {
            0 => FromD(b, t, p),
            1 => FromD(q, b, p),
            2 => FromD(p, b, t),
            3 => FromD(p, q, b),
            4 => FromD(t, p, b),
            _ => FromD(b, p, q)
        };
    }

    private static Color FromD(double r, double g, double b)
        => Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));

    private static void ColorToHsb(Color c, out double h, out double s, out double b)
    {
        double r = c.R / 255.0, g = c.G / 255.0, bl = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, bl));
        double min = Math.Min(r, Math.Min(g, bl));
        double delta = max - min;
        b = max;
        s = max == 0 ? 0 : delta / max;
        if (delta == 0) { h = 0; return; }
        if (max == r)      h = 60 * (((g - bl) / delta) % 6);
        else if (max == g) h = 60 * (((bl - r) / delta) + 2);
        else               h = 60 * (((r - g) / delta) + 4);
        if (h < 0) h += 360;
    }
}
