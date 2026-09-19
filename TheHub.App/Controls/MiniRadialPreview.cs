using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TheHub.Common.Models;

using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using ColorConverter = System.Windows.Media.ColorConverter;
using WpfFlowDirection = System.Windows.FlowDirection;

namespace TheHub.App.Controls;

/// <summary>
/// Mini radial wheel preview used in workspace cards (matching Rovyl's workspaces UI).
/// Renders a dashed circular orbit ring, a colored center hub dot, and mini icons/glyphs
/// for each application assigned to the workspace (or "empty" text if no apps).
/// </summary>
public class MiniRadialPreview : FrameworkElement
{
    public static readonly DependencyProperty AppsProperty =
        DependencyProperty.Register(nameof(Apps), typeof(IEnumerable<AppShortcut>), typeof(MiniRadialPreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CenterColorProperty =
        DependencyProperty.Register(nameof(CenterColor), typeof(string), typeof(MiniRadialPreview),
            new FrameworkPropertyMetadata("#3B82F6", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(MiniRadialPreview),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<AppShortcut>? Apps
    {
        get => (IEnumerable<AppShortcut>?)GetValue(AppsProperty);
        set => SetValue(AppsProperty, value);
    }

    public string CenterColor
    {
        get => (string)GetValue(CenterColorProperty);
        set => SetValue(CenterColorProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private readonly Typeface _glyphTypeface = new(
        new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly Typeface _textTypeface = new(
        new FontFamily("Segoe UI Variable Display, Segoe UI"),
        FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth > 0 ? ActualWidth : 180;
        double h = ActualHeight > 0 ? ActualHeight : 105;
        Point center = new(w / 2.0, h / 2.0);
        double orbitRadius = 32.0;

        // 1. Subtle Dashed Orbit Ring
        var orbitColor = Color.FromArgb(42, 255, 255, 255);
        var orbitPen = new Pen(new SolidColorBrush(orbitColor), 1.0)
        {
            DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
        };
        dc.DrawEllipse(null, orbitPen, center, orbitRadius, orbitRadius);

        // 2. Center Hub Colored Dot
        Brush centerBrush;
        try
        {
            var hex = string.IsNullOrWhiteSpace(CenterColor) ? "#3B82F6" : CenterColor;
            var col = (Color)ColorConverter.ConvertFromString(hex);
            centerBrush = new SolidColorBrush(col);
        }
        catch
        {
            centerBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
        }

        // Ambient center shadow
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), null, center, 8, 8);
        // Solid center dot
        dc.DrawEllipse(centerBrush, null, center, 6, 6);

        var appList = Apps != null ? new List<AppShortcut>(Apps) : new List<AppShortcut>();
        int count = appList.Count;

        if (count == 0)
        {
            // Draw "empty" text below center hub
            var emptyText = new FormattedText("empty", CultureInfo.CurrentCulture,
                WpfFlowDirection.LeftToRight, _textTypeface, 9.5,
                new SolidColorBrush(Color.FromRgb(113, 113, 122)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(emptyText, new Point(center.X - emptyText.Width / 2.0, center.Y + 9));
            return;
        }

        // 3. Mini App Tiles positioned along the dashed orbit
        double step = (2.0 * Math.PI) / count;
        double tileRadius = 9.5;

        for (int i = 0; i < count; i++)
        {
            double angle = -Math.PI / 2.0 + (i * step);
            double px = center.X + orbitRadius * Math.Cos(angle);
            double py = center.Y + orbitRadius * Math.Sin(angle);
            Point itemCenter = new(px, py);

            // Mini rounded tile background
            var tileBg = new SolidColorBrush(Color.FromArgb(235, 24, 24, 30));
            var tileBorder = new Pen(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), 0.8);
            dc.DrawEllipse(tileBg, tileBorder, itemCenter, tileRadius, tileRadius);

            // Icon / Glyph inside mini tile
            string glyph = !string.IsNullOrWhiteSpace(appList[i].IconGlyph)
                ? appList[i].IconGlyph
                : (!string.IsNullOrWhiteSpace(appList[i].Title) ? appList[i].Title.Substring(0, 1).ToUpper() : "•");

            var glyphText = new FormattedText(glyph, CultureInfo.CurrentCulture,
                WpfFlowDirection.LeftToRight, _glyphTypeface, 8.5,
                Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(glyphText, new Point(itemCenter.X - glyphText.Width / 2.0, itemCenter.Y - glyphText.Height / 2.0));
        }
    }
}
