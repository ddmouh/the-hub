using System.IO;
using TheHub.Core.Bus;

namespace TheHub.Core.Services;

/// <summary>
/// Monitors the user's Downloads folder for in-progress and completed downloads.
/// Publishes DownloadProgress events to IHubBus.
/// </summary>
public class DownloadWatcher : IDisposable
{
    private readonly IHubBus _bus;
    private readonly string _downloadsPath;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer _pollTimer;
    private readonly Dictionary<string, long> _activeDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public DownloadWatcher(IHubBus? bus = null, string? customDownloadsPath = null)
    {
        _bus = bus ?? HubBus.Default;
        _downloadsPath = customDownloadsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (Directory.Exists(_downloadsPath))
        {
            try
            {
                _watcher = new FileSystemWatcher(_downloadsPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnFileActivity;
                _watcher.Changed += OnFileActivity;
                _watcher.Renamed += OnFileRenamed;
            }
            catch { }
        }

        _pollTimer = new System.Threading.Timer(OnPollDownloads, null, 1500, 1500);
    }

    private void OnFileActivity(object sender, FileSystemEventArgs e)
    {
        if (IsPartialFile(e.FullPath))
        {
            lock (_lock)
            {
                _activeDownloads[e.FullPath] = GetFileSize(e.FullPath);
            }
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        lock (_lock)
        {
            if (_activeDownloads.ContainsKey(e.OldFullPath))
            {
                _activeDownloads.Remove(e.OldFullPath);
                // Download finished (renamed from .crdownload/.part to final name)
                string finalName = Path.GetFileName(e.FullPath);
                _bus.Publish(new DownloadProgress(finalName, 100, true));
            }
        }
    }

    private void OnPollDownloads(object? state)
    {
        lock (_lock)
        {
            if (_activeDownloads.Count == 0) return;

            var keys = _activeDownloads.Keys.ToList();
            foreach (var file in keys)
            {
                if (!File.Exists(file))
                {
                    _activeDownloads.Remove(file);
                    continue;
                }

                long currentSize = GetFileSize(file);
                long prevSize = _activeDownloads[file];
                _activeDownloads[file] = currentSize;

                string cleanName = CleanDownloadFileName(file);
                // Estimated progress: publish active growth
                int progress = prevSize > 0 && currentSize > prevSize ? Math.Min(95, (int)((currentSize % 1000000) / 10000)) : 50;
                _bus.Publish(new DownloadProgress(cleanName, progress, false));
            }
        }
    }

    private static bool IsPartialFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".crdownload" or ".part" or ".opdownload" or ".download";
    }

    private static string CleanDownloadFileName(string path)
    {
        string name = Path.GetFileName(path);
        if (name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase))
            return name[..^11];
        if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return name[..^5];
        if (name.EndsWith(".opdownload", StringComparison.OrdinalIgnoreCase))
            return name[..^11];
        return name;
    }

    private static long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _pollTimer.Dispose();
    }
}
