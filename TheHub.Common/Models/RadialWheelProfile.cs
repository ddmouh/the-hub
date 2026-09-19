using System.Collections.ObjectModel;
using System.ComponentModel;

namespace TheHub.Common.Models;

/// <summary>
/// Dedicated profile for the Radial Launcher wheel.
/// Contains the shortcut slices displayed on the circular wheel.
/// Completely independent from batch-launch Workspaces.
/// </summary>
public class RadialWheelProfile : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Main Wheel";
    public string IconGlyph { get; set; } = "🎯";
    public string AccentColor { get; set; } = "#3B82F6";
    public bool IsActive { get; set; } = true;
    public bool IsVisibleInSwitcher { get; set; } = true;
    public int HotkeyIndex { get; set; } = 1;
    public ObservableCollection<AppShortcut> Slices { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public ObservableCollection<AppShortcut> Apps
    {
        get => Slices;
        set => Slices = value;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ShortcutCountText => IsActive ? $"Current · {Slices.Count} shortcuts" : $"{Slices.Count} shortcuts";

    [System.Text.Json.Serialization.JsonIgnore]
    public string CardBackground => IsActive ? "#141418" : "#0E0E12";

    [System.Text.Json.Serialization.JsonIgnore]
    public string BorderBrushColor => IsActive ? "#FFFFFF" : "#24242C";

    [System.Text.Json.Serialization.JsonIgnore]
    public string BorderThicknessValue => IsActive ? "2" : "1";

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifySlicesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Slices)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Apps)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShortcutCountText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardBackground)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BorderBrushColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BorderThicknessValue)));
    }
}
