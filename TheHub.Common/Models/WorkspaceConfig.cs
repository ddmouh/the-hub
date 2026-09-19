using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TheHub.Common.Models;

public class WorkspaceConfig : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _iconGlyph = "📁";
    private string _accentColor = "#3B82F6";
    private List<AppShortcut> _apps = new();
    private bool _pauseWallpapers = true;
    private bool _hideTaskbar = false;
    private bool _enableDoNotDisturb = false;
    private int? _targetVolume = null;
    private bool _isActive = false;
    private bool _isVisibleInSwitcher = true;
    private bool _isDefaultActive = false;
    private int _hotkeyIndex = 1;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string IconGlyph
    {
        get => _iconGlyph;
        set { _iconGlyph = value; OnPropertyChanged(); }
    }

    public string AccentColor
    {
        get => _accentColor;
        set
        {
            _accentColor = value;
            OnPropertyChanged();
        }
    }

    public List<AppShortcut> Apps
    {
        get => _apps;
        set
        {
            _apps = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShortcutCountText));
        }
    }

    public bool PauseWallpapers
    {
        get => _pauseWallpapers;
        set { _pauseWallpapers = value; OnPropertyChanged(); }
    }

    public bool HideTaskbar
    {
        get => _hideTaskbar;
        set { _hideTaskbar = value; OnPropertyChanged(); }
    }

    public bool EnableDoNotDisturb
    {
        get => _enableDoNotDisturb;
        set { _enableDoNotDisturb = value; OnPropertyChanged(); }
    }

    public int? TargetVolume
    {
        get => _targetVolume;
        set { _targetVolume = value; OnPropertyChanged(); }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShortcutCountText));
                OnPropertyChanged(nameof(BorderBrushColor));
                OnPropertyChanged(nameof(BorderThicknessValue));
                OnPropertyChanged(nameof(CardBackground));
            }
        }
    }

    public bool IsVisibleInSwitcher
    {
        get => _isVisibleInSwitcher;
        set { _isVisibleInSwitcher = value; OnPropertyChanged(); }
    }

    public bool IsDefaultActive
    {
        get => _isDefaultActive;
        set { _isDefaultActive = value; OnPropertyChanged(); }
    }

    public int HotkeyIndex
    {
        get => _hotkeyIndex;
        set { _hotkeyIndex = value; OnPropertyChanged(); }
    }

    // Visual helper properties for XAML data binding
    public string ShortcutCountText => IsActive ? $"Current · {Apps?.Count ?? 0} shortcuts" : $"{Apps?.Count ?? 0} shortcuts";
    public string BorderBrushColor => IsActive ? "#FFFFFF" : "#1F1F24";
    public string BorderThicknessValue => IsActive ? "2" : "1";
    public string CardBackground => IsActive ? "#141418" : "#111114";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void NotifyAppsChanged()
    {
        OnPropertyChanged(nameof(Apps));
        OnPropertyChanged(nameof(ShortcutCountText));
    }
}
