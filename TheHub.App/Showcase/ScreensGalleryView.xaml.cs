using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using RadioButton = System.Windows.Controls.RadioButton;
using Image = System.Windows.Controls.Image;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TheHub.App.Showcase;

/// <summary>
/// Interaction logic for ScreensGalleryView.xaml.
/// Provides a Mobbin/PageFlows-style interactive screen showcase gallery with live previews and automated PNG export.
/// </summary>
public partial class ScreensGalleryView : UserControl
{
    private readonly ViewSnapshotRegistry _registry;
    private string _selectedCategory = "All";
    private string _searchQuery = "";
    private ViewSnapshotItem? _activeLightboxItem;

    public ScreensGalleryView()
    {
        InitializeComponent();
        _registry = new ViewSnapshotRegistry();
        Loaded += OnScreensGalleryViewLoaded;
        PreviewKeyDown += OnScreensGalleryViewPreviewKeyDown;
    }

    private void OnScreensGalleryViewPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            OnExportAllClick(this, new RoutedEventArgs());
        }
    }

    private void OnScreensGalleryViewLoaded(object sender, RoutedEventArgs e)
    {
        RefreshGalleryCards();
        GenerateAllThumbnailsBackground();
    }

    private void RefreshGalleryCards()
    {
        IEnumerable<ViewSnapshotItem> items = _registry.GetAllSnapshots();

        if (!string.Equals(_selectedCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(x => string.Equals(x.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            var q = _searchQuery.Trim().ToLowerInvariant();
            items = items.Where(x =>
                x.Title.ToLowerInvariant().Contains(q) ||
                x.Category.ToLowerInvariant().Contains(q) ||
                x.Description.ToLowerInvariant().Contains(q) ||
                x.StateTag.ToLowerInvariant().Contains(q) ||
                x.Tags.Any(t => t.ToLowerInvariant().Contains(q)));
        }

        var list = items.ToList();
        ShowcaseCardsItemsControl.ItemsSource = list;

        TxtGalleryStatus.Text = $"Showing {list.Count} of {_registry.GetAllSnapshots().Count} screens ({_selectedCategory} category). Click 'Inspect' to jump directly into that view.";
    }

    private void GenerateAllThumbnailsBackground()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            foreach (var item in _registry.GetAllSnapshots())
            {
                if (item.ThumbnailSource == null)
                {
                    try
                    {
                        item.ThumbnailSource = ScreenSnapshotExporter.RenderSnapshotBitmap(item, 400, 240);
                    }
                    catch { }
                }
            }
        }));
    }

    #region Category & Search Filtering

    private void OnCategoryFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string cat)
        {
            _selectedCategory = cat;
            RefreshGalleryCards();
        }
    }

    private void OnGallerySearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = GallerySearchBox.Text ?? "";
        RefreshGalleryCards();
    }

    #endregion

    #region Inspect in App Navigation

    private void OnInspectClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ViewSnapshotItem item)
        {
            TriggerInspect(item);
        }
    }

    private void TriggerInspect(ViewSnapshotItem item)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        if (mainWindow != null)
        {
            item.InspectAction?.Invoke(mainWindow);
            TxtGalleryStatus.Text = $"Navigated to '{item.Title}' in The Hub.";
        }
    }

    #endregion

    #region Screenshot Export Handlers

    private async void OnExportAllClick(object sender, RoutedEventArgs e)
    {
        try
        {
            TxtGalleryStatus.Text = "📸 Rendering and exporting all registered UI screens to PNG...";
            var items = _registry.GetAllSnapshots();

            var progress = new Progress<(int current, int total, string title)>(p =>
            {
                TxtGalleryStatus.Text = $"📸 Exporting ({p.current}/{p.total}): {p.title}...";
            });

            var files = await ScreenSnapshotExporter.ExportAllAsync(items, progress);

            TxtGalleryStatus.Text = $"✅ Successfully exported {files.Count} screens to {ScreenSnapshotExporter.GetExportDirectory()}";
            ScreenSnapshotExporter.OpenScreenshotsDirectory();
        }
        catch (Exception ex)
        {
            TxtGalleryStatus.Text = $"❌ Export error: {ex.Message}";
        }
    }

    private void OnExportSingleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ViewSnapshotItem item)
        {
            try
            {
                var path = ScreenSnapshotExporter.ExportSnapshotToPng(item);
                TxtGalleryStatus.Text = $"✅ Saved '{item.Title}' -> {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                TxtGalleryStatus.Text = $"❌ Failed to export: {ex.Message}";
            }
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        ScreenSnapshotExporter.OpenScreenshotsDirectory();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in _registry.GetAllSnapshots())
        {
            item.ThumbnailSource = null;
        }
        GenerateAllThumbnailsBackground();
        RefreshGalleryCards();
        TxtGalleryStatus.Text = "🔄 Refreshed all showcase snapshot cards.";
    }

    #endregion

    #region Lightbox Full-Screen Modal

    private void OnCardPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ViewSnapshotItem item)
        {
            OpenLightbox(item);
        }
    }

    private void OnZoomCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ViewSnapshotItem item)
        {
            OpenLightbox(item);
        }
    }

    private void OpenLightbox(ViewSnapshotItem item)
    {
        _activeLightboxItem = item;
        TxtLightboxTitle.Text = item.Title;
        TxtLightboxCategory.Text = $"{item.Category} • {item.StateTag}";
        TxtLightboxDescription.Text = item.Description;

        if (item.LiveCaptureFactory != null)
        {
            try
            {
                var bmp = item.LiveCaptureFactory();
                if (bmp != null)
                {
                    LightboxPreviewHost.Content = new Image { Source = bmp, Stretch = System.Windows.Media.Stretch.Uniform, SnapsToDevicePixels = true };
                    LightboxModal.Visibility = Visibility.Visible;
                    return;
                }
            }
            catch { }
        }

        if (item.PreviewElementFactory != null)
        {
            try
            {
                var previewEl = item.PreviewElementFactory();
                LightboxPreviewHost.Content = previewEl;
            }
            catch
            {
                LightboxPreviewHost.Content = new Image { Source = item.ThumbnailSource, Stretch = System.Windows.Media.Stretch.Uniform, SnapsToDevicePixels = true };
            }
        }
        else
        {
            LightboxPreviewHost.Content = new Image { Source = item.ThumbnailSource, Stretch = System.Windows.Media.Stretch.Uniform, SnapsToDevicePixels = true };
        }

        LightboxModal.Visibility = Visibility.Visible;
    }

    private void OnCloseLightboxClick(object sender, RoutedEventArgs e)
    {
        LightboxModal.Visibility = Visibility.Collapsed;
        LightboxPreviewHost.Content = null;
    }

    private void OnLightboxExportClick(object sender, RoutedEventArgs e)
    {
        if (_activeLightboxItem != null)
        {
            try
            {
                var path = ScreenSnapshotExporter.ExportSnapshotToPng(_activeLightboxItem);
                TxtGalleryStatus.Text = $"✅ Saved '{_activeLightboxItem.Title}' -> {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                TxtGalleryStatus.Text = $"❌ Failed to export: {ex.Message}";
            }
        }
    }

    private void OnLightboxInspectClick(object sender, RoutedEventArgs e)
    {
        if (_activeLightboxItem != null)
        {
            LightboxModal.Visibility = Visibility.Collapsed;
            TriggerInspect(_activeLightboxItem);
        }
    }

    #endregion
}
