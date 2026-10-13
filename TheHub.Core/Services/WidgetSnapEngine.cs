using System.Drawing;

namespace TheHub.Core.Services;

public record SnapResult(
    RectangleF SnappedBounds,
    bool SnappedToEdge,
    bool SnappedToWidget,
    bool SnappedToGrid
);

/// <summary>
/// Pure snapping engine for freeform desktop widgets.
/// Computes screen edge alignment, inter-widget magnetic snapping, and grid alignment.
/// </summary>
public static class WidgetSnapEngine
{
    public const int DefaultSnapThreshold = 16;
    public const int DefaultGridSize = 16;

    /// <summary>
    /// Snaps a widget's position against screen edges, sibling widgets, and optional grid.
    /// </summary>
    public static SnapResult ComputeSnap(
        RectangleF target,
        RectangleF screenBounds,
        IEnumerable<RectangleF>? otherWidgets = null,
        int threshold = DefaultSnapThreshold,
        bool enableGrid = false,
        int gridSize = DefaultGridSize)
    {
        float x = target.X;
        float y = target.Y;
        float width = target.Width;
        float height = target.Height;

        bool snappedEdge = false;
        bool snappedWidget = false;
        bool snappedGrid = false;

        // 1. Inter-Widget Magnetic Snapping
        if (otherWidgets != null)
        {
            foreach (var other in otherWidgets)
            {
                // Align left to other's right
                if (Math.Abs(x - other.Right) <= threshold)
                {
                    x = other.Right;
                    snappedWidget = true;
                }
                // Align right to other's left
                else if (Math.Abs((x + width) - other.Left) <= threshold)
                {
                    x = other.Left - width;
                    snappedWidget = true;
                }
                // Align left edges
                else if (Math.Abs(x - other.Left) <= threshold)
                {
                    x = other.Left;
                    snappedWidget = true;
                }
                // Align right edges
                else if (Math.Abs((x + width) - other.Right) <= threshold)
                {
                    x = other.Right - width;
                    snappedWidget = true;
                }

                // Align top to other's bottom
                if (Math.Abs(y - other.Bottom) <= threshold)
                {
                    y = other.Bottom;
                    snappedWidget = true;
                }
                // Align bottom to other's top
                else if (Math.Abs((y + height) - other.Top) <= threshold)
                {
                    y = other.Top - height;
                    snappedWidget = true;
                }
                // Align top edges
                else if (Math.Abs(y - other.Top) <= threshold)
                {
                    y = other.Top;
                    snappedWidget = true;
                }
                // Align bottom edges
                else if (Math.Abs((y + height) - other.Bottom) <= threshold)
                {
                    y = other.Bottom - height;
                    snappedWidget = true;
                }
            }
        }

        // 2. Screen Edge Snapping (Takes priority over inter-widget if near boundary)
        // Left screen edge
        if (Math.Abs(x - screenBounds.Left) <= threshold)
        {
            x = screenBounds.Left;
            snappedEdge = true;
        }
        // Right screen edge
        else if (Math.Abs((x + width) - screenBounds.Right) <= threshold)
        {
            x = screenBounds.Right - width;
            snappedEdge = true;
        }

        // Top screen edge
        if (Math.Abs(y - screenBounds.Top) <= threshold)
        {
            y = screenBounds.Top;
            snappedEdge = true;
        }
        // Bottom screen edge
        else if (Math.Abs((y + height) - screenBounds.Bottom) <= threshold)
        {
            y = screenBounds.Bottom - height;
            snappedEdge = true;
        }

        // 3. Optional Grid Alignment
        if (enableGrid && !snappedEdge && !snappedWidget && gridSize > 0)
        {
            float gridX = (float)(Math.Round((x - screenBounds.Left) / gridSize) * gridSize + screenBounds.Left);
            float gridY = (float)(Math.Round((y - screenBounds.Top) / gridSize) * gridSize + screenBounds.Top);

            x = gridX;
            y = gridY;
            snappedGrid = true;
        }

        return new SnapResult(
            new RectangleF(x, y, width, height),
            snappedEdge,
            snappedWidget,
            snappedGrid
        );
    }
}
