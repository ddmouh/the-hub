namespace TheHub.Common.Models;

public class AppShortcut
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "⚡";
    public string Category { get; set; } = "General";
    public string ShortcutType { get; set; } = "Application"; // Application, URL, Folder
    public int Order { get; set; }
}
