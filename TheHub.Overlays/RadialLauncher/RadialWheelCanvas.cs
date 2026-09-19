using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TheHub.Common.Models;

namespace TheHub.Overlays.RadialLauncher;

/// <summary>
/// Rovyl-style radial wheel: floating glass tiles orbiting a center hub,
/// connected by a dashed orbit ring, with full customizable visual metrics and themes.
/// </summary>
public class RadialWheelCanvas : Canvas
{
    #region Dependency Properties

    public static readonly DependencyProperty ShortcutsProperty =
        DependencyProperty.Register(nameof(Shortcuts), typeof(List<AppShortcut>), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(new List<AppShortcut>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightedIndexProperty =
        DependencyProperty.Register(nameof(HighlightedIndex), typeof(int), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OuterRadiusProperty =
        DependencyProperty.Register(nameof(OuterRadius), typeof(double), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InnerRadiusProperty =
        DependencyProperty.Register(nameof(InnerRadius), typeof(double), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(60.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(64.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TargetSpacingProperty =
        DependencyProperty.Register(nameof(TargetSpacing), typeof(double), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoverColorHexProperty =
        DependencyProperty.Register(nameof(HoverColorHex), typeof(string), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AppearanceThemeProperty =
        DependencyProperty.Register(nameof(AppearanceTheme), typeof(string), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata("Black", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PersistentLabelsProperty =
        DependencyProperty.Register(nameof(PersistentLabels), typeof(bool), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WheelOpacityFactorProperty =
        DependencyProperty.Register(nameof(WheelOpacityFactor), typeof(double), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(0.85, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedLabelProperty =
        DependencyProperty.Register(nameof(SelectedLabel), typeof(string), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata("Center", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BloomProgressProperty =
        DependencyProperty.Register(nameof(BloomProgress), typeof(double), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CenterColorProperty =
        DependencyProperty.Register(nameof(CenterColor), typeof(Brush), typeof(RadialWheelCanvas),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush CenterColor
    {
        get => (Brush)GetValue(CenterColorProperty);
        set => SetValue(CenterColorProperty, value);
    }

    public List<AppShortcut> Shortcuts
    {
        get => (List<AppShortcut>)GetValue(ShortcutsProperty);
        set => SetValue(ShortcutsProperty, value);
    }

    public int HighlightedIndex
    {
        get => (int)GetValue(HighlightedIndexProperty);
        set => SetValue(HighlightedIndexProperty, value);
    }

    public double OuterRadius
    {
        get => (double)GetValue(OuterRadiusProperty);
        set => SetValue(OuterRadiusProperty, value);
    }

    /// <summary>Radius of the center disc (default 60px -> 120px diameter).</summary>
    public double InnerRadius
    {
        get => (double)GetValue(InnerRadiusProperty);
        set => SetValue(InnerRadiusProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double TargetSpacing
    {
        get => (double)GetValue(TargetSpacingProperty);
        set => SetValue(TargetSpacingProperty, value);
    }

    public string HoverColorHex
    {
        get => (string)GetValue(HoverColorHexProperty);
        set => SetValue(HoverColorHexProperty, value);
    }

    public string AppearanceTheme
    {
        get => (string)GetValue(AppearanceThemeProperty);
        set => SetValue(AppearanceThemeProperty, value);
    }

    public bool PersistentLabels
    {
        get => (bool)GetValue(PersistentLabelsProperty);
        set => SetValue(PersistentLabelsProperty, value);
    }

    public double WheelOpacityFactor
    {
        get => (double)GetValue(WheelOpacityFactorProperty);
        set => SetValue(WheelOpacityFactorProperty, value);
    }

    /// <summary>Right side of the bottom pill: shows the hovered item name or "Center".</summary>
    public string SelectedLabel
    {
        get => (string)GetValue(SelectedLabelProperty);
        set => SetValue(SelectedLabelProperty, value);
    }

    /// <summary>0→1 bloom animation progress. 0 = collapsed at center, 1 = fully expanded.</summary>
    public double BloomProgress
    {
        get => (double)GetValue(BloomProgressProperty);
        set => SetValue(BloomProgressProperty, value);
    }

    #endregion

    // ── Typefaces ──────────────────────────────────────────────────────
    private readonly Typeface _iconTypeface = new(
        new FontFamily("Segoe UI Emoji"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Typeface _labelTypeface = new(
        new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private readonly Typeface _pillTypeface = new(
        new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private const double TileHoverScale = 1.06;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double cx = ActualWidth / 2.0;
        double cy = ActualHeight / 2.0;
        var center = new Point(cx, cy);

        var shortcuts = Shortcuts;
        int count = shortcuts?.Count ?? 0;
        double bloom = Math.Clamp(BloomProgress, 0.0, 1.0);
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double baseRadius = OuterRadius + (TargetSpacing - 10.0);
        double effectiveRadius = baseRadius * (0.78 + 0.22 * bloom);

        // ── 1. Dashed orbit ring ──────────────────────────────────────
        DrawOrbitRing(dc, center, baseRadius * (0.90 + 0.10 * bloom), bloom);

        // ── 2. Floating glass tiles ───────────────────────────────────
        if (count > 0)
        {
            DrawFloatingTiles(dc, center, shortcuts!, count, bloom, effectiveRadius, ppd);
        }
        else if (bloom > 0.3)
        {
            var emptyText = new FormattedText(
                "Wheel is empty",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _labelTypeface,
                11.5,
                new SolidColorBrush(Color.FromArgb((byte)(160 * bloom), 140, 140, 155)),
                ppd);

            double ty = center.Y + (InnerRadius > 0 ? InnerRadius : 60.0) + 16;
            dc.DrawText(emptyText, new Point(center.X - emptyText.Width / 2.0, ty));
        }

        // ── 3. Center hub (solid disc + logo) ─────────────────────────
        DrawCenterHub(dc, center, bloom, ppd);

        // ── 4. Bottom dual pill badge (rock solid position with smooth fade-in)
        DrawBottomPill(dc, cx, cy, baseRadius, bloom, ppd);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CENTER HUB — solid disc with Rovyl logo mark (Strict 120px diameter default)
    // ═══════════════════════════════════════════════════════════════════
    private void DrawCenterHub(DrawingContext dc, Point center, double bloom, double ppd)
    {
        double baseHubRadius = InnerRadius > 0 ? InnerRadius : 60.0;
        double hubRadius = baseHubRadius * (0.90 + 0.10 * bloom);
        bool isWhiteTheme = string.Equals(AppearanceTheme, "White", StringComparison.OrdinalIgnoreCase);

        // Ambient shadow
        byte shadowAlpha = (byte)(Math.Clamp(bloom, 0.0, 1.0) * (isWhiteTheme ? 40 : 50));
        Color shadowColor = isWhiteTheme
            ? Color.FromArgb(shadowAlpha, 0, 0, 0)
            : Color.FromArgb(shadowAlpha, 255, 255, 255);
        dc.DrawEllipse(new SolidColorBrush(shadowColor), null, center, hubRadius + 8, hubRadius + 8);

        // Main center disc
        Brush discBrush;
        if (CenterColor != null && CenterColor != Brushes.White)
        {
            discBrush = CenterColor;
        }
        else
        {
            discBrush = isWhiteTheme
                ? new SolidColorBrush(Color.FromArgb(255, 250, 250, 252))
                : Brushes.White;
        }

        Pen? discPen = isWhiteTheme
            ? new Pen(new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), 1.5)
            : new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.0);

        dc.DrawEllipse(discBrush, discPen, center, hubRadius, hubRadius);

        // Rovyl logo mark — geometric 3-pronged mark in center
        Color logoColor = isWhiteTheme ? Color.FromArgb(255, 20, 20, 25) : Color.FromArgb(255, 10, 10, 12);
        var logoBrush = new SolidColorBrush(logoColor);
        double penThick = Math.Max(2.5, hubRadius * 0.05);
        var logoPen = new Pen(logoBrush, penThick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        double armLen = hubRadius * 0.36;
        double armOff = hubRadius * 0.26;

        // Top-right arm
        dc.DrawLine(logoPen, new Point(center.X + 1, center.Y - 1), new Point(center.X + armLen, center.Y - armLen));
        // Bottom-left arm
        dc.DrawLine(logoPen, new Point(center.X - 1, center.Y + 1), new Point(center.X - armOff, center.Y + armLen));
        // Left arm
        dc.DrawLine(logoPen, new Point(center.X - 1, center.Y - 1), new Point(center.X - armLen, center.Y - armOff + 2));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ORBIT RING — dashed circle connecting tile centers
    // ═══════════════════════════════════════════════════════════════════
    private void DrawOrbitRing(DrawingContext dc, Point center, double radius, double bloom)
    {
        if (radius < 10) return;

        bool isWhiteTheme = string.Equals(AppearanceTheme, "White", StringComparison.OrdinalIgnoreCase);
        byte alpha = (byte)(Math.Clamp(bloom, 0.0, 1.0) * (isWhiteTheme ? 40 : 45));
        Color orbitColor = isWhiteTheme
            ? Color.FromArgb(alpha, 0, 0, 0)
            : Color.FromArgb(alpha, 255, 255, 255);

        var orbitPen = new Pen(new SolidColorBrush(orbitColor), 1.0)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, 0)
        };

        dc.DrawEllipse(null, orbitPen, center, radius, radius);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FLOATING TILES — glass rounded rectangles around orbit
    // ═══════════════════════════════════════════════════════════════════
    private void DrawFloatingTiles(DrawingContext dc, Point center, List<AppShortcut> shortcuts, int count,
        double bloom, double effectiveRadius, double ppd)
    {
        double angleStep = 360.0 / count;
        double tileDim = Math.Clamp(IconSize > 0 ? IconSize : 64.0, 36.0, 92.0);
        double cornerRadius = Math.Clamp(tileDim * 0.28, 10.0, 26.0);

        bool isWhiteTheme = string.Equals(AppearanceTheme, "White", StringComparison.OrdinalIgnoreCase);
        double opacity = Math.Clamp(WheelOpacityFactor, 0.35, 1.0);

        Color hoverColor = ParseColor(HoverColorHex, Colors.White);

        // Calculate tile background and borders
        Color tileBg = isWhiteTheme
            ? Color.FromArgb((byte)(255 * (opacity * 0.90)), 245, 245, 250)
            : Color.FromArgb((byte)(255 * (opacity * 0.85)), 26, 26, 31);

        Color tileBorder = isWhiteTheme
            ? Color.FromArgb(35, 0, 0, 0)
            : Color.FromArgb(22, 255, 255, 255);

        Color tileHoverBg = hoverColor;
        Color tileHoverBorder = Color.FromArgb(180, hoverColor.R, hoverColor.G, hoverColor.B);

        // Contrast-aware hover text color
        double luminance = (0.299 * hoverColor.R + 0.587 * hoverColor.G + 0.114 * hoverColor.B) / 255.0;
        Brush hoverIconFg = luminance > 0.55 ? new SolidColorBrush(Color.FromArgb(255, 12, 12, 16)) : Brushes.White;
        Brush normalIconFg = isWhiteTheme
            ? new SolidColorBrush(Color.FromArgb(240, 24, 24, 28))
            : Brushes.White;

        for (int i = 0; i < count; i++)
        {
            bool isHovered = (i == HighlightedIndex);
            double angleDeg = (i * angleStep) - 90.0;
            double angleRad = angleDeg * Math.PI / 180.0;

            double tileX = center.X + effectiveRadius * Math.Cos(angleRad);
            double tileY = center.Y + effectiveRadius * Math.Sin(angleRad);

            tileX = Math.Round(tileX * ppd) / ppd;
            tileY = Math.Round(tileY * ppd) / ppd;

            double scale = (0.78 + 0.22 * bloom) * (isHovered ? TileHoverScale : 1.0);
            double scaledW = tileDim * scale;
            double scaledH = tileDim * scale;

            var tileRect = new Rect(
                tileX - scaledW / 2,
                tileY - scaledH / 2,
                scaledW,
                scaledH
            );

            var roundedRect = new RectangleGeometry(tileRect, cornerRadius * scale, cornerRadius * scale);

            Color currentBg = isHovered ? tileHoverBg : tileBg;
            Color currentBorder = isHovered ? tileHoverBorder : tileBorder;
            var tileBrush = new SolidColorBrush(currentBg);
            var tilePen = new Pen(new SolidColorBrush(currentBorder), isHovered ? 1.5 : 1.0);

            // Hover glow shadow
            if (isHovered)
            {
                var glowBrush = new SolidColorBrush(Color.FromArgb(60, hoverColor.R, hoverColor.G, hoverColor.B));
                var glowRect = new Rect(tileRect.X - 5, tileRect.Y - 5, tileRect.Width + 10, tileRect.Height + 10);
                dc.DrawRoundedRectangle(glowBrush, null, glowRect, cornerRadius + 5, cornerRadius + 5);
            }

            dc.DrawGeometry(tileBrush, tilePen, roundedRect);

            // ── Icon glyph ────────────────────────────────────────────
            var item = shortcuts[i];
            double glyphFontSize = (isHovered ? tileDim * 0.40 : tileDim * 0.35) * Math.Clamp(0.75 + 0.25 * bloom, 0.5, 1.0);
            Brush iconFg = isHovered ? hoverIconFg : normalIconFg;

            var glyphText = new FormattedText(
                item.IconGlyph,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _iconTypeface,
                glyphFontSize,
                iconFg,
                ppd);

            dc.DrawText(glyphText, new Point(
                tileX - glyphText.Width / 2,
                tileY - glyphText.Height / 2));

            // ── Label pill ───────────────────────────────────────────
            bool shouldDrawLabel = (PersistentLabels && bloom > 0.6) || (isHovered && bloom > 0.6);
            if (shouldDrawLabel)
            {
                double labelOpacity = isHovered ? 1.0 : 0.8;
                DrawTileLabel(dc, item.Title, tileX, tileY, angleDeg, scaledW, scaledH, isHovered, isWhiteTheme, ppd, labelOpacity);
            }
        }
    }

    private void DrawTileLabel(DrawingContext dc, string label, double tileX, double tileY,
        double angleDeg, double tileW, double tileH, bool isHovered, bool isWhiteTheme, double ppd, double opacity)
    {
        if (string.IsNullOrWhiteSpace(label)) return;

        double fontSize = 11.0;
        Brush fg = isHovered
            ? (isWhiteTheme ? Brushes.Black : Brushes.White)
            : (isWhiteTheme
                ? new SolidColorBrush(Color.FromArgb((byte)(200 * opacity), 30, 30, 35))
                : new SolidColorBrush(Color.FromArgb((byte)(180 * opacity), 170, 170, 180)));

        var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, _labelTypeface, fontSize, fg, ppd);

        double cos = Math.Cos(angleDeg * Math.PI / 180.0);
        double sin = Math.Sin(angleDeg * Math.PI / 180.0);
        double edgeOffset = tileW / 2 + 8;

        double labelX, labelY;
        if (Math.Abs(cos) > 0.35)
        {
            labelX = cos > 0
                ? tileX + edgeOffset
                : tileX - edgeOffset - text.Width;
            labelY = tileY - text.Height / 2;
        }
        else
        {
            labelX = tileX - text.Width / 2;
            labelY = sin < 0
                ? tileY - tileH / 2 - text.Height - 4
                : tileY + tileH / 2 + 4;
        }

        var pillRect = new Rect(labelX - 6, labelY - 2, text.Width + 12, text.Height + 4);
        Color pillColor = isWhiteTheme
            ? Color.FromArgb((byte)(210 * opacity), 235, 235, 240)
            : Color.FromArgb((byte)(160 * opacity), 18, 18, 22);

        dc.DrawRoundedRectangle(new SolidColorBrush(pillColor), null, pillRect, 6, 6);
        dc.DrawText(text, new Point(labelX, labelY));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BOTTOM PILL — dual-segment badge "The Hub | Center"
    // ═══════════════════════════════════════════════════════════════════
    private void DrawBottomPill(DrawingContext dc, double cx, double cy, double baseRadius, double bloom, double ppd)
    {
        double pillOpacity = Math.Clamp((bloom - 0.20) / 0.80, 0.0, 1.0);
        if (pillOpacity <= 0) return;

        bool isWhiteTheme = string.Equals(AppearanceTheme, "White", StringComparison.OrdinalIgnoreCase);

        string leftText = "The Hub";
        string rightText = SelectedLabel ?? "Center";

        byte leftAlpha = (byte)(255 * pillOpacity);
        byte rightAlpha = (byte)(isWhiteTheme ? 200 * pillOpacity : 190 * pillOpacity);

        Brush leftFg = isWhiteTheme
            ? new SolidColorBrush(Color.FromArgb(leftAlpha, 20, 20, 25))
            : new SolidColorBrush(Color.FromArgb(leftAlpha, 255, 255, 255));
        Brush rightFg = isWhiteTheme
            ? new SolidColorBrush(Color.FromArgb(rightAlpha, 70, 70, 80))
            : new SolidColorBrush(Color.FromArgb(rightAlpha, 161, 161, 170));

        var leftFmt = new FormattedText(leftText, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, _pillTypeface, 12, leftFg, ppd);
        var rightFmt = new FormattedText(rightText, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, _pillTypeface, 12, rightFg, ppd);

        double padX = 14;
        double padY = 6;
        double gap = 12;
        double dividerW = 1;

        double totalW = padX + leftFmt.Width + gap + dividerW + gap + rightFmt.Width + padX;
        double totalH = Math.Max(leftFmt.Height, rightFmt.Height) + padY * 2;

        double pillX = cx - totalW / 2;
        double pillY = cy + baseRadius + (IconSize / 2.0) + 20;

        if (pillY + totalH > ActualHeight - 10) pillY = ActualHeight - totalH - 10;

        var pillRect = new Rect(pillX, pillY, totalW, totalH);
        Color pillBg = isWhiteTheme
            ? Color.FromArgb((byte)(220 * pillOpacity), 240, 240, 246)
            : Color.FromArgb((byte)(210 * pillOpacity), 24, 24, 28);
        Color pillBorder = isWhiteTheme
            ? Color.FromArgb((byte)(40 * pillOpacity), 0, 0, 0)
            : Color.FromArgb((byte)(30 * pillOpacity), 255, 255, 255);

        dc.DrawRoundedRectangle(new SolidColorBrush(pillBg), new Pen(new SolidColorBrush(pillBorder), 1), pillRect, totalH / 2, totalH / 2);

        double textY = pillY + (totalH - leftFmt.Height) / 2;
        dc.DrawText(leftFmt, new Point(pillX + padX, textY));

        double divX = pillX + padX + leftFmt.Width + gap;
        Color divColor = isWhiteTheme
            ? Color.FromArgb((byte)(50 * pillOpacity), 0, 0, 0)
            : Color.FromArgb((byte)(40 * pillOpacity), 255, 255, 255);
        dc.DrawLine(
            new Pen(new SolidColorBrush(divColor), dividerW),
            new Point(divX, pillY + padY + 2),
            new Point(divX, pillY + totalH - padY - 2));

        dc.DrawText(rightFmt, new Point(divX + gap, textY));
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return fallback;
        }
    }
}
