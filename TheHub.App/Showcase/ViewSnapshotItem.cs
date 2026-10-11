using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TheHub.App.Showcase;

/// <summary>
/// Represents a visual screen snapshot or sub-view state in The Hub UI Flow Showcase.
/// </summary>
public class ViewSnapshotItem : INotifyPropertyChanged
{
    private ImageSource? _thumbnailSource;
    private bool _isGeneratingThumbnail;

    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string BadgeColor { get; set; } = "#3B82F6";
    public string StateTag { get; set; } = "Default";
    public string Description { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();

    public double TargetWidth { get; set; } = 800;
    public double TargetHeight { get; set; } = 500;

    /// <summary>
    /// Factory delegate to construct a high-fidelity visual preview element.
    /// </summary>
    public Func<FrameworkElement>? PreviewElementFactory { get; set; }

    /// <summary>
    /// Factory delegate to capture live rasterized screenshot directly from the active window/page.
    /// </summary>
    public Func<BitmapSource?>? LiveCaptureFactory { get; set; }

    /// <summary>
    /// Delegate to navigate/switch the active application to this exact screen state.
    /// </summary>
    public Action<MainWindow>? InspectAction { get; set; }

    /// <summary>
    /// Rendered cached thumbnail bitmap for responsive gallery rendering.
    /// </summary>
    public ImageSource? ThumbnailSource
    {
        get => _thumbnailSource;
        set
        {
            _thumbnailSource = value;
            OnPropertyChanged();
        }
    }

    public bool IsGeneratingThumbnail
    {
        get => _isGeneratingThumbnail;
        set
        {
            _isGeneratingThumbnail = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
