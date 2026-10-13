using System.ComponentModel;

namespace TheHub.Common.Models;

public enum WallpaperType
{
    Static,
    Animated
}

public class WallpaperItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Untitled Wallpaper";
    public string FilePath { get; set; } = string.Empty;
    public WallpaperType Type { get; set; } = WallpaperType.Static;
    public bool IsActive { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    [System.Text.Json.Serialization.JsonIgnore]
    public string TypeBadgeText => Type == WallpaperType.Animated ? "🎬 LIVE" : "🖼️ STATIC";

    [System.Text.Json.Serialization.JsonIgnore]
    public string TypeBadgeBackground => Type == WallpaperType.Animated ? "#7C3AED" : "#2563EB";

    [System.Text.Json.Serialization.JsonIgnore]
    public string CardBorderBrush => IsActive ? "#10B981" : "#27272A";

    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusText => IsActive ? "Active" : "Apply";

    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusBackground => IsActive ? "#065F46" : "#27272A";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMissingFile => !string.IsNullOrWhiteSpace(FilePath) && !System.IO.File.Exists(FilePath);

    [System.Text.Json.Serialization.JsonIgnore]
    public string WarningBadgeText => IsMissingFile ? "⚠️ MISSING FILE" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBorderBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBackground)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMissingFile)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WarningBadgeText)));
    }
}
