using System.Diagnostics;
using System.Text;
using TheHub.Common.Config;
using TheHub.Core.Native;

namespace TheHub.Core.Monitors;

public record AmbientStatusInfo(string Glyph, string Title, string Subtitle, string Category);

public class ActiveAppMonitor : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private string _lastSignature = "";

    public event Action<AmbientStatusInfo>? AmbientStatusChanged;
    public AmbientStatusInfo CurrentStatus { get; private set; } = new("⚡", "The Hub", "Companion Ready", "System");

    public ActiveAppMonitor()
    {
        _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _timer.Change(500, 1500);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnTick(object? state)
    {
        try
        {
            var settings = SettingsManager.Current;
            if (!settings.IslandShowActiveApp) return;

            IntPtr hwnd = Win32Api.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            // Get window title
            var sb = new StringBuilder(512);
            Win32Api.GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString().Trim();

            // Get process name
            Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = "";
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName.ToLowerInvariant();
            }
            catch { }

            if (string.IsNullOrWhiteSpace(title) || processName == "thehub" || processName == "thehub.app" || processName == "explorer")
            {
                return;
            }

            string signature = $"{processName}::{title}";
            if (signature == _lastSignature) return;
            _lastSignature = signature;

            var status = ClassifyWindow(processName, title);
            CurrentStatus = status;
            AmbientStatusChanged?.Invoke(status);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ActiveAppMonitor] Error: {ex.Message}");
        }
    }

    private AmbientStatusInfo ClassifyWindow(string processName, string title)
    {
        // 1. YouTube / Streams / Videos in browsers
        if (title.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Twitch", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Kick", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Netflix", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Anime", StringComparison.OrdinalIgnoreCase))
        {
            string clean = CleanBrowserTitle(title);
            return new AmbientStatusInfo("📺", clean, "Streaming", "Stream");
        }

        // 2. Music / Audio
        if (processName.Contains("spotify") || title.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            return new AmbientStatusInfo("🎵", CleanMusicTitle(title), "Spotify Music", "Music");
        }

        // 3. Coding / Development
        if (processName.Contains("code") || processName.Contains("antigravity") ||
            processName.Contains("devenv") || processName.Contains("rider") || processName.Contains("terminal"))
        {
            string clean = CleanDevTitle(title);
            return new AmbientStatusInfo("💻", clean, "Developing", "Coding");
        }

        // 4. Gaming
        if (processName.Contains("steam") || processName.Contains("valorant") ||
            processName.Contains("riot") || processName.Contains("epicgames") ||
            processName.Contains("minecraft") || processName.Contains("roblox") ||
            processName.Contains("overwatch") || processName.Contains("cs2") || processName.Contains("gta"))
        {
            return new AmbientStatusInfo("🎮", title.Length > 28 ? title[..28] : title, "Gaming Active", "Gaming");
        }

        // 5. Social & Communication
        if (processName.Contains("discord"))
        {
            return new AmbientStatusInfo("💬", "Discord", "Voice & Chat", "Social");
        }
        if (processName.Contains("telegram") || processName.Contains("whatsapp") || processName.Contains("slack"))
        {
            return new AmbientStatusInfo("💬", title.Length > 25 ? title[..25] : title, "Messaging", "Social");
        }

        // 6. Generic Browsing
        if (processName is "chrome" or "msedge" or "firefox" or "brave" or "opera")
        {
            string clean = CleanBrowserTitle(title);
            return new AmbientStatusInfo("🌐", clean, "Web Browsing", "Browser");
        }

        // 7. General App
        string displayTitle = title.Length > 26 ? title[..26] + "…" : title;
        return new AmbientStatusInfo("⚡", displayTitle, processName, "App");
    }

    private static string CleanMusicTitle(string title)
    {
        string result = title.Replace(" - Spotify", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(result) || result.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
            return "Spotify";
        return result.Length > 28 ? result[..28] + "…" : result;
    }

    private static string CleanBrowserTitle(string title)
    {
        string[] suffixes = [" - Google Chrome", " - Microsoft Edge", " - Mozilla Firefox", " - Brave", " - YouTube", " - Twitch"];
        string result = title;
        foreach (var s in suffixes)
        {
            if (result.EndsWith(s, StringComparison.OrdinalIgnoreCase))
            {
                result = result[..^s.Length].Trim();
            }
        }
        return result.Length > 30 ? result[..30] + "…" : result;
    }

    private static string CleanDevTitle(string title)
    {
        string[] suffixes = [" - Antigravity IDE", " - Visual Studio Code", " - Visual Studio", " - Windows PowerShell"];
        string result = title;
        foreach (var s in suffixes)
        {
            if (result.EndsWith(s, StringComparison.OrdinalIgnoreCase))
            {
                result = result[..^s.Length].Trim();
            }
        }
        return result.Length > 28 ? result[..28] + "…" : result;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
