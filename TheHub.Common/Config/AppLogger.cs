using System.IO;

namespace TheHub.Common.Config;

public static class AppLogger
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TheHub",
        "app_debug.log"
    );

    private static readonly object LockObj = new();

    public static void Log(string message)
    {
        try
        {
            lock (LockObj)
            {
                string dir = Path.GetDirectoryName(LogFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using var fs = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs) { AutoFlush = true };
                sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
