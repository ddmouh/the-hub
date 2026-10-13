using System;
using System.IO;
using System.Text.Json;

namespace TheHub.Core.AI;

public class AIAuditLogger
{
    private readonly string _logPath;
    private readonly object _lock = new();
    private readonly int _maxEntriesBeforeRotate;

    public AIAuditLogger(string? customLogPath = null, int maxEntriesBeforeRotate = 10000)
    {
        _maxEntriesBeforeRotate = maxEntriesBeforeRotate;
        if (!string.IsNullOrWhiteSpace(customLogPath))
        {
            _logPath = customLogPath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var hubFolder = Path.Combine(appData, "TheHub", "Logs");
            Directory.CreateDirectory(hubFolder);
            _logPath = Path.Combine(hubFolder, "audit.jsonl");
        }

        var dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public string LogFilePath => _logPath;

    public void Log(AIAuditEntry entry)
    {
        lock (_lock)
        {
            try
            {
                RotateIfNeeded();
                var json = JsonSerializer.Serialize(entry);
                File.AppendAllText(_logPath, json + Environment.NewLine);
            }
            catch
            {
                // Never crash on audit failure
            }
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (File.Exists(_logPath))
            {
                var fileInfo = new FileInfo(_logPath);
                // Rotate if over 5MB
                if (fileInfo.Length > 5 * 1024 * 1024)
                {
                    var backupPath = _logPath + $".{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                    File.Move(_logPath, backupPath, true);
                }
            }
        }
        catch
        {
        }
    }

    public IReadOnlyList<AIAuditEntry> ReadRecentEntries(int maxCount = 50)
    {
        var entries = new List<AIAuditEntry>();
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_logPath)) return entries;

                var lines = File.ReadAllLines(_logPath);
                var start = Math.Max(0, lines.Length - maxCount);
                for (int i = lines.Length - 1; i >= start; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var entry = JsonSerializer.Deserialize<AIAuditEntry>(lines[i]);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch
            {
            }
        }
        return entries;
    }
}
