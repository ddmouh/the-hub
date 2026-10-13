namespace TheHub.Core.Bus;

public sealed record MediaChanged(
    string? Title,
    string? Artist,
    bool IsPlaying,
    int PositionMs = 0,
    int DurationMs = 0,
    string? Glyph = "🎵") : IHubEvent;

public sealed record VolumeChanged(int VolumePercent, bool IsMuted) : IHubEvent;

public sealed record MicMuteChanged(bool IsMuted) : IHubEvent;

public sealed record BrightnessChanged(int BrightnessPercent) : IHubEvent;

public sealed record UsbChanged(string DeviceName, bool IsConnected) : IHubEvent;

public sealed record ClipboardChanged(string TextPreview) : IHubEvent;

public sealed record DownloadProgress(string FileName, int ProgressPercent, bool IsCompleted) : IHubEvent;

public sealed record PrayerTimeDue(string PrayerName, string TimeString) : IHubEvent;

public sealed record PomodoroTick(int RemainingSeconds, string Phase) : IHubEvent;

public sealed record WorkspaceChanged(string WorkspaceName, string? AccentColor) : IHubEvent;

public sealed record FullscreenChanged(bool IsFullscreen, IntPtr Hwnd) : IHubEvent;

public sealed record ActiveAppChanged(string AppName, string? WindowTitle) : IHubEvent;

public sealed record PowerStateChanged(bool IsOnBattery, int BatteryPercent) : IHubEvent;

public sealed record SettingsChanged(string SectionName) : IHubEvent;
