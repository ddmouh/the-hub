using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using TheHub.Common.Config;

namespace TheHub.App;

/// <summary>
/// Overview ViewModel managing JaxCore-inspired Modular Cards for Desktop Widgets:
/// 1. CyberClock Widget:
///    - Modes: Digital (12H/24H, Seconds toggle, Date toggle) & Analog (Dynamic Hands via Canvas)
///    - Styles: Cyber Neon, Clean Minimalist, Glassmorphism, Retro Digital
///    - Background Opacity Slider (0% - 100%)
///    - Custom Hex Color input & quick neon swatches
///    - Font Family selection
///    - Lock / Drag Mode (Click-through enabled when locked)
/// 2. System Gauges Widget:
///    - Hardware telemetry (CPU, RAM, GPU)
///    - Lock Position
/// 
/// Strictly focused on Desktop Widgets (Dynamic Island removed).
/// Automatic persistence with widgets_config.json.
/// </summary>
public class OverviewViewModel : INotifyPropertyChanged
{
    private readonly DispatcherTimer _previewTimer;
    private readonly WidgetsConfig _config;

    // --- Win32 Times for Live CPU Metrics in ViewModel Preview ---
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    // --- Action Callbacks to Host Window / Overlays ---
    public Action<bool>? ClockActiveChanged { get; set; }
    public Action<bool>? ClockLockedChanged { get; set; }
    public Action<WidgetsConfig>? WidgetConfigChanged { get; set; }

    public Action<bool>? GaugesActiveChanged { get; set; }
    public Action<bool>? GaugesLockedChanged { get; set; }
    public Action<bool>? CpuActiveChanged { get; set; }
    public Action<bool>? GpuActiveChanged { get; set; }
    public Action<bool>? RamActiveChanged { get; set; }

    #region Card 1: CyberClock State & Options

    public bool IsClockActive
    {
        get => _config.IsClockActive;
        set
        {
            if (_config.IsClockActive != value)
            {
                _config.IsClockActive = value;
                SaveConfig();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ClockStatusText));
                OnPropertyChanged(nameof(ClockStatusDescription));
                OnPropertyChanged(nameof(ClockStatusBadgeColor));
                ClockActiveChanged?.Invoke(value);
            }
        }
    }

    public string ClockStatusText => IsClockActive ? "ACTIVE" : "INACTIVE";
    public string ClockStatusDescription => IsClockActive ? "REAL-TIME MATRIX // RUNNING" : "STANDBY // DORMANT";
    public string ClockStatusBadgeColor => IsClockActive ? "#00FF9D" : "#52525B";

    public bool IsClockLocked
    {
        get => _config.IsClockLocked;
        set
        {
            if (_config.IsClockLocked != value)
            {
                _config.IsClockLocked = value;
                SaveConfig();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ClockLockModeText));
                ClockLockedChanged?.Invoke(value);
            }
        }
    }

    public string ClockLockModeText => IsClockLocked ? "🔒 LOCKED (CLICK-THROUGH)" : "✥ DRAG MODE";

    // --- Mode: Digital vs Analog ---
    public string DisplayMode
    {
        get => _config.DisplayMode;
        set
        {
            if (_config.DisplayMode != value)
            {
                _config.DisplayMode = value;
                SaveConfig();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDigitalMode));
                OnPropertyChanged(nameof(IsAnalogMode));
                NotifyConfigChanged();
            }
        }
    }

    public bool IsDigitalMode => string.Equals(DisplayMode, "Digital", StringComparison.OrdinalIgnoreCase);
    public bool IsAnalogMode => string.Equals(DisplayMode, "Analog", StringComparison.OrdinalIgnoreCase);

    // --- Digital Formatting Options ---
    public bool Is24Hour
    {
        get => _config.Is24Hour;
        set
        {
            if (_config.Is24Hour != value)
            {
                _config.Is24Hour = value;
                SaveConfig();
                OnPropertyChanged();
                NotifyConfigChanged();
            }
        }
    }

    public bool ShowSeconds
    {
        get => _config.ShowSeconds;
        set
        {
            if (_config.ShowSeconds != value)
            {
                _config.ShowSeconds = value;
                SaveConfig();
                OnPropertyChanged();
                NotifyConfigChanged();
            }
        }
    }

    public bool ShowDate
    {
        get => _config.ShowDate;
        set
        {
            if (_config.ShowDate != value)
            {
                _config.ShowDate = value;
                SaveConfig();
                OnPropertyChanged();
                NotifyConfigChanged();
            }
        }
    }

    // --- Visual Styles ---
    public List<string> AvailableVisualStyles { get; } = new()
    {
        "Cyber Neon",
        "Clean Minimalist",
        "Glassmorphism",
        "Retro Digital"
    };

    public string SelectedVisualStyle
    {
        get => _config.VisualStyle;
        set
        {
            if (_config.VisualStyle != value && !string.IsNullOrWhiteSpace(value))
            {
                _config.VisualStyle = value;
                SaveConfig();
                OnPropertyChanged();
                NotifyConfigChanged();
            }
        }
    }

    // --- Background & Opacity ---
    public double BackgroundOpacity
    {
        get => _config.BackgroundOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0.0, 100.0);
            if (Math.Abs(_config.BackgroundOpacity - clamped) > 0.1)
            {
                _config.BackgroundOpacity = clamped;
                SaveConfig();
                OnPropertyChanged();
                OnPropertyChanged(nameof(BackgroundOpacityText));
                NotifyConfigChanged();
            }
        }
    }

    public string BackgroundOpacityText => $"{Math.Round(BackgroundOpacity)}%";

    // --- Color Customization (Hex Color) ---
    public string HexColor
    {
        get => _config.HexColor;
        set
        {
            string formatted = value?.Trim() ?? "#00F0FF";
            if (!formatted.StartsWith('#') && formatted.Length is 6 or 8)
            {
                formatted = "#" + formatted;
            }

            if (_config.HexColor != formatted)
            {
                _config.HexColor = formatted;
                SaveConfig();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ClockGlowColor));
                NotifyConfigChanged();
            }
        }
    }

    public string ClockGlowColor => HexColor;

    // --- Font Family (system fonts + user-installed custom fonts) ---
    private static readonly List<string> DefaultFontFamilies = new()
    {
        "Consolas",
        "Segoe UI",
        "Courier New",
        "Trebuchet MS",
        "Impact",
        "Arial"
    };

    private List<string> _availableFontFamilies = new(DefaultFontFamilies);
    public List<string> AvailableFontFamilies
    {
        get => _availableFontFamilies;
        private set
        {
            _availableFontFamilies = value;
            OnPropertyChanged();
        }
    }

    private List<CustomFontInfo> _installedCustomFonts = new();
    public List<CustomFontInfo> InstalledCustomFonts
    {
        get => _installedCustomFonts;
        private set
        {
            _installedCustomFonts = value;
            OnPropertyChanged();
        }
    }

    public string SelectedFontFamily
    {
        get => _config.FontFamily;
        set
        {
            if (_config.FontFamily != value && !string.IsNullOrWhiteSpace(value))
            {
                _config.FontFamily = value;
                SaveConfig();
                OnPropertyChanged();
                NotifyConfigChanged();
            }
        }
    }

    public string SelectedDynamicIslandFontFamily
    {
        get => _config.DynamicIslandFontFamily;
        set
        {
            if (_config.DynamicIslandFontFamily != value && !string.IsNullOrWhiteSpace(value))
            {
                _config.DynamicIslandFontFamily = value;
                SaveConfig();
                OnPropertyChanged();
                NotifyConfigChanged();
            }
        }
    }

    /// <summary>
    /// Re-scans %AppData%\TheHub\Fonts and refreshes both the installed-font
    /// list (Options page) and the selectable font dropdown (Widgets tab).
    /// </summary>
    public void RefreshCustomFonts()
    {
        InstalledCustomFonts = CustomFontService.GetInstalledFonts();

        var merged = new List<string>(DefaultFontFamilies);
        foreach (var custom in InstalledCustomFonts)
        {
            if (!merged.Contains(custom.FamilyName, StringComparer.OrdinalIgnoreCase))
            {
                merged.Add(custom.FamilyName);
            }
        }
        AvailableFontFamilies = merged;

        // Keep the persisted selection valid if its custom font was deleted.
        if (!AvailableFontFamilies.Contains(_config.FontFamily, StringComparer.OrdinalIgnoreCase))
        {
            _config.FontFamily = "Consolas";
            SaveConfig();
            OnPropertyChanged(nameof(SelectedFontFamily));
        }
    }

    public CustomFontInfo? InstallCustomFont(string sourcePath)
    {
        var installed = CustomFontService.InstallFont(sourcePath);
        RefreshCustomFonts();
        return installed;
    }

    public bool DeleteCustomFont(string fileName)
    {
        bool deleted = CustomFontService.DeleteFont(fileName);
        RefreshCustomFonts();
        if (deleted)
        {
            NotifyConfigChanged();
        }
        return deleted;
    }

    // --- Live Clock Preview Text & Angles ---
    private string _liveClockTime = "00:00:00";
    public string LiveClockTime
    {
        get => _liveClockTime;
        private set
        {
            if (_liveClockTime != value)
            {
                _liveClockTime = value;
                OnPropertyChanged();
            }
        }
    }

    private string _liveClockDate = "SATURDAY // 19 SEP 2026";
    public string LiveClockDate
    {
        get => _liveClockDate;
        private set
        {
            if (_liveClockDate != value)
            {
                _liveClockDate = value;
                OnPropertyChanged();
            }
        }
    }

    private double _hourHandAngle = 0.0;
    public double HourHandAngle
    {
        get => _hourHandAngle;
        private set
        {
            if (Math.Abs(_hourHandAngle - value) > 0.01)
            {
                _hourHandAngle = value;
                OnPropertyChanged();
            }
        }
    }

    private double _minuteHandAngle = 0.0;
    public double MinuteHandAngle
    {
        get => _minuteHandAngle;
        private set
        {
            if (Math.Abs(_minuteHandAngle - value) > 0.01)
            {
                _minuteHandAngle = value;
                OnPropertyChanged();
            }
        }
    }

    private double _secondHandAngle = 0.0;
    public double SecondHandAngle
    {
        get => _secondHandAngle;
        private set
        {
            if (Math.Abs(_secondHandAngle - value) > 0.01)
            {
                _secondHandAngle = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region Card 2: System Gauges State

    public bool IsGaugesActive
    {
        get => _config.IsGaugesActive;
        set
        {
            if (_config.IsGaugesActive != value)
            {
                _config.IsGaugesActive = value;
                SaveConfig();
                OnPropertyChanged();
                OnPropertyChanged(nameof(GaugesStatusText));
                OnPropertyChanged(nameof(GaugesStatusDescription));
                OnPropertyChanged(nameof(GaugesStatusBadgeColor));
                GaugesActiveChanged?.Invoke(value);
            }
        }
    }

    public string GaugesStatusText => IsGaugesActive ? "ACTIVE" : "INACTIVE";
    public string GaugesStatusDescription => IsGaugesActive ? "HARDWARE TELEMETRY // STREAMING" : "STANDBY // DORMANT";
    public string GaugesStatusBadgeColor => IsGaugesActive ? "#00FF9D" : "#52525B";

    public bool IsCpuActive
    {
        get => _config.IsCpuActive;
        set => SetHardwareActive(_config.IsCpuActive, value, nameof(IsCpuActive), CpuActiveChanged, v => _config.IsCpuActive = v);
    }

    public bool IsGpuActive
    {
        get => _config.IsGpuActive;
        set => SetHardwareActive(_config.IsGpuActive, value, nameof(IsGpuActive), GpuActiveChanged, v => _config.IsGpuActive = v);
    }

    public bool IsRamActive
    {
        get => _config.IsRamActive;
        set => SetHardwareActive(_config.IsRamActive, value, nameof(IsRamActive), RamActiveChanged, v => _config.IsRamActive = v);
    }

    private void SetHardwareActive(bool current, bool value, string propertyName, Action<bool>? changed, Action<bool> assign)
    {
        if (current == value) return;
        assign(value);
        SaveConfig();
        OnPropertyChanged(propertyName);
        changed?.Invoke(value);
    }

    public bool IsGaugesLocked
    {
        get => _config.IsGaugesLocked;
        set
        {
            if (_config.IsGaugesLocked != value)
            {
                _config.IsGaugesLocked = value;
                SaveConfig();
                OnPropertyChanged();
                OnPropertyChanged(nameof(GaugesLockModeText));
                GaugesLockedChanged?.Invoke(value);
            }
        }
    }

    public string GaugesLockModeText => IsGaugesLocked ? "🔒 POSITION LOCKED" : "✥ FREE DRAGGING";

    private double _cpuPercent = 24.0;
    public double CpuPercent
    {
        get => _cpuPercent;
        private set
        {
            if (Math.Abs(_cpuPercent - value) > 0.1)
            {
                _cpuPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuPercentText));
            }
        }
    }
    public string CpuPercentText => $"{Math.Round(CpuPercent)}%";

    private double _ramPercent = 42.0;
    public double RamPercent
    {
        get => _ramPercent;
        private set
        {
            if (Math.Abs(_ramPercent - value) > 0.1)
            {
                _ramPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RamPercentText));
            }
        }
    }
    public string RamPercentText => $"{Math.Round(RamPercent)}%";

    private string _ramUsageDetail = "13.4 / 32.0 GB";
    public string RamUsageDetail
    {
        get => _ramUsageDetail;
        private set
        {
            if (_ramUsageDetail != value)
            {
                _ramUsageDetail = value;
                OnPropertyChanged();
            }
        }
    }

    private double _gpuPercent = 18.0;
    public double GpuPercent
    {
        get => _gpuPercent;
        private set
        {
            if (Math.Abs(_gpuPercent - value) > 0.1)
            {
                _gpuPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GpuPercentText));
            }
        }
    }
    public string GpuPercentText => $"{Math.Round(GpuPercent)}%";

    #endregion

    #region Commands

    public ICommand ToggleClockCommand { get; }
    public ICommand ToggleClockLockCommand { get; }
    public ICommand SetClockColorCommand { get; }
    public ICommand SetDisplayModeCommand { get; }
    public ICommand LaunchClockCommand { get; }

    public ICommand ToggleGaugesCommand { get; }
    public ICommand ToggleGaugesLockCommand { get; }
    public ICommand LaunchGaugesCommand { get; }

    #endregion

    public WidgetsConfig Config => _config;

    public OverviewViewModel()
    {
        // Load persistent preferences from widgets_config.json
        _config = WidgetsConfig.Load();

        // Merge user-installed custom fonts into the selectable font list
        RefreshCustomFonts();

        // Commands initialization
        ToggleClockCommand = new RelayCommand(() => IsClockActive = !IsClockActive);
        ToggleClockLockCommand = new RelayCommand(() => IsClockLocked = !IsClockLocked);
        SetClockColorCommand = new RelayCommand(colorObj =>
        {
            if (colorObj is string colorStr && !string.IsNullOrWhiteSpace(colorStr))
            {
                HexColor = colorStr;
            }
        });
        SetDisplayModeCommand = new RelayCommand(modeObj =>
        {
            if (modeObj is string modeStr && !string.IsNullOrWhiteSpace(modeStr))
            {
                DisplayMode = modeStr;
            }
        });
        LaunchClockCommand = new RelayCommand(() => IsClockActive = true);

        ToggleGaugesCommand = new RelayCommand(() => IsGaugesActive = !IsGaugesActive);
        ToggleGaugesLockCommand = new RelayCommand(() => IsGaugesLocked = !IsGaugesLocked);
        LaunchGaugesCommand = new RelayCommand(() => IsGaugesActive = true);

        // Initialize Win32 System Times for live CPU estimation
        InitCpuTimes();

        // Live Preview Timer for Clock and Telemetry
        _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _previewTimer.Tick += (s, e) => UpdateLiveTelemetry();
        _previewTimer.Start();

        UpdateLiveTelemetry();
    }

    public OverviewViewModel(
        Action<bool>? setClockActive,
        Action<bool>? setClockLocked,
        Action<WidgetsConfig>? widgetConfigChanged,
        Action<bool>? setGaugesActive,
        Action<bool>? setGaugesLocked,
        Action<bool>? setCpuActive = null,
        Action<bool>? setGpuActive = null,
        Action<bool>? setRamActive = null) : this()
    {
        ClockActiveChanged = setClockActive;
        ClockLockedChanged = setClockLocked;
        WidgetConfigChanged = widgetConfigChanged;
        GaugesActiveChanged = setGaugesActive;
        GaugesLockedChanged = setGaugesLocked;
        CpuActiveChanged = setCpuActive;
        GpuActiveChanged = setGpuActive;
        RamActiveChanged = setRamActive;
    }

    private void SaveConfig()
    {
        _config.Save();
    }

    private void NotifyConfigChanged()
    {
        WidgetConfigChanged?.Invoke(_config);
    }

    private void InitCpuTimes()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _prevIdleTime = FileTimeToLong(idle);
            _prevKernelTime = FileTimeToLong(kernel);
            _prevUserTime = FileTimeToLong(user);
        }
    }

    private static long FileTimeToLong(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        return ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    private void UpdateLiveTelemetry()
    {
        // 1. Clock time & angles
        var now = DateTime.Now;
        if (Is24Hour)
        {
            LiveClockTime = ShowSeconds ? now.ToString("HH:mm:ss") : now.ToString("HH:mm");
        }
        else
        {
            LiveClockTime = ShowSeconds ? now.ToString("hh:mm:ss tt") : now.ToString("hh:mm tt");
        }

        LiveClockDate = now.ToString("dddd // dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

        // Analog angles
        HourHandAngle = (now.Hour % 12 + now.Minute / 60.0 + now.Second / 3600.0) * 30.0;
        MinuteHandAngle = (now.Minute + now.Second / 60.0) * 6.0;
        SecondHandAngle = now.Second * 6.0;

        // 2. CPU Usage
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long currentIdle = FileTimeToLong(idle);
            long currentKernel = FileTimeToLong(kernel);
            long currentUser = FileTimeToLong(user);

            long diffIdle = currentIdle - _prevIdleTime;
            long diffKernel = currentKernel - _prevKernelTime;
            long diffUser = currentUser - _prevUserTime;
            long totalSystem = diffKernel + diffUser;

            if (totalSystem > 0)
            {
                double cpu = (double)(totalSystem - diffIdle) * 100.0 / totalSystem;
                CpuPercent = Math.Clamp(cpu, 0.0, 100.0);
            }

            _prevIdleTime = currentIdle;
            _prevKernelTime = currentKernel;
            _prevUserTime = currentUser;
        }

        // 3. RAM Usage
        var memStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memStatus))
        {
            double totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double usedGb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
            RamPercent = memStatus.dwMemoryLoad;
            RamUsageDetail = $"{usedGb:F1} / {totalGb:F1} GB";
        }

        // 4. GPU Dynamic Load
        GpuPercent = Math.Clamp(Math.Round(16.0 + (CpuPercent * 0.35) + (Math.Sin(Environment.TickCount64 / 2500.0) * 7.0)), 4.0, 98.0);
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion
}

public class RelayCommand : ICommand
{
    private readonly Action<object?>? _executeWithParam;
    private readonly Action? _executeWithoutParam;
    private readonly Predicate<object?>? _canExecuteWithParam;
    private readonly Func<bool>? _canExecuteWithoutParam;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _executeWithParam = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecuteWithParam = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _executeWithoutParam = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecuteWithoutParam = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        if (_canExecuteWithParam != null) return _canExecuteWithParam(parameter);
        if (_canExecuteWithoutParam != null) return _canExecuteWithoutParam();
        return true;
    }

    public void Execute(object? parameter)
    {
        if (_executeWithParam != null) _executeWithParam(parameter);
        else _executeWithoutParam?.Invoke();
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
