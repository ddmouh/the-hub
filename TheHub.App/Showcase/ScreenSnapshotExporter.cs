using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
using Point = System.Windows.Point;

namespace TheHub.App.Showcase;

/// <summary>
/// High-fidelity WPF screen renderer and automated PNG exporter.
/// Utilizes RenderTargetBitmap and PngBitmapEncoder for crystal-clear exports.
/// </summary>
public static class ScreenSnapshotExporter
{
    private const string DefaultScreenshotSubFolder = "Screenshots/UI_Flow";

    public static string GetExportDirectory()
    {
        var appPath = AppDomain.CurrentDomain.BaseDirectory;
        var exportPath = Path.Combine(appPath, DefaultScreenshotSubFolder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(exportPath))
        {
            Directory.CreateDirectory(exportPath);
        }
        return exportPath;
    }

    /// <summary>
    /// Renders a snapshot item to a BitmapSource on the WPF UI thread.
    /// </summary>
    public static BitmapSource RenderSnapshotBitmap(ViewSnapshotItem item, double width = 0, double height = 0)
    {
        // 1. Live rasterized capture from the active application window/page
        if (item.LiveCaptureFactory != null)
        {
            var liveBmp = item.LiveCaptureFactory();
            if (liveBmp != null) return liveBmp;
        }

        // 2. Cached bitmap source
        if (item.ThumbnailSource is BitmapSource cachedBmp && item.PreviewElementFactory == null)
            return cachedBmp;

        if (item.PreviewElementFactory == null)
            throw new InvalidOperationException($"No PreviewElementFactory or LiveCaptureFactory defined for {item.Id}");

        var element = item.PreviewElementFactory();

        if (element is System.Windows.Controls.Image img && img.Source is BitmapSource imgBmp)
            return imgBmp;

        double targetW = width > 0 ? width : (item.TargetWidth > 0 ? item.TargetWidth : 800);
        double targetH = height > 0 ? height : (item.TargetHeight > 0 ? item.TargetHeight : 500);

        var renderSize = new Size(targetW, targetH);
        var renderRect = new Rect(renderSize);

        // Force complete headless layout passes on the unmounted UserControl/element
        element.Measure(renderSize);
        element.Arrange(renderRect);
        element.UpdateLayout();

        // Wrap in a presentation container with dark backdrop to give transparent widgets/overlays context
        var container = new Border
        {
            Width = targetW,
            Height = targetH,
            Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x11)),
            Child = element,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };

        container.Measure(renderSize);
        container.Arrange(renderRect);
        container.UpdateLayout();

        int pixelW = Math.Max(1, (int)Math.Ceiling(targetW));
        int pixelH = Math.Max(1, (int)Math.Ceiling(targetH));

        var renderBitmap = new RenderTargetBitmap(
            pixelW,
            pixelH,
            96,
            96,
            PixelFormats.Pbgra32);

        renderBitmap.Render(container);
        renderBitmap.Freeze(); // Make cross-thread accessible

        return renderBitmap;
    }

    /// <summary>
    /// Exports a single snapshot card to a PNG file.
    /// </summary>
    public static string ExportSnapshotToPng(ViewSnapshotItem item, string? customFolder = null)
    {
        var folder = customFolder ?? GetExportDirectory();
        var bitmap = RenderSnapshotBitmap(item);

        var safeCategory = SanitizeFilename(item.Category);
        var safeId = SanitizeFilename(item.Id);
        var fileName = $"{safeCategory}_{safeId}.png";
        var fullPath = Path.Combine(folder, fileName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            encoder.Save(stream);
        }

        return fullPath;
    }

    /// <summary>
    /// Exports all registered snapshots into the UI_Flow screenshots directory.
    /// </summary>
    public static async Task<List<string>> ExportAllAsync(
        IEnumerable<ViewSnapshotItem> items,
        IProgress<(int current, int total, string title)>? progress = null,
        string? customFolder = null)
    {
        var folder = customFolder ?? GetExportDirectory();
        var itemList = items.ToList();
        var exportedFiles = new List<string>();

        for (int i = 0; i < itemList.Count; i++)
        {
            var item = itemList[i];
            progress?.Report((i + 1, itemList.Count, item.Title));

            // Run on UI thread if needed for WPF visual creation
            string exportedPath = string.Empty;
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    exportedPath = ExportSnapshotToPng(item, folder);
                });
            }
            else
            {
                exportedPath = ExportSnapshotToPng(item, folder);
            }

            exportedFiles.Add(exportedPath);
            await Task.Delay(20); // allow UI to refresh smoothly
        }

        return exportedFiles;
    }

    /// <summary>
    /// Opens the exported screenshots directory in Windows Explorer.
    /// </summary>
    public static void OpenScreenshotsDirectory()
    {
        try
        {
            var path = GetExportDirectory();
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch { }
    }

    private static string SanitizeFilename(string name)
    {
        var invalids = Path.GetInvalidFileNameChars();
        var clean = string.Join("_", name.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return clean.Replace(" ", "_").Replace("&", "and");
    }
}
