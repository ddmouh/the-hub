namespace TheHub.Common.Models;

public enum IslandEventType
{
    Media,
    Volume,
    Usb,
    Clipboard,
    Timer,
    SystemAlert,
    WorkspaceChanged
}

public class DynamicIslandEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public IslandEventType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Glyph { get; set; } = "🔔";
    public double Progress { get; set; } = 0.0; // 0.0 to 1.0 or percentage
    public int Value { get; set; } = 0; // E.g., Volume level (0-100)
    public int DisplayDurationMs { get; set; } = 3000;
    public bool IsPersistent { get; set; } = false; // Persistent for timers or ongoing media
    public bool IsPlaying { get; set; } = false;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
