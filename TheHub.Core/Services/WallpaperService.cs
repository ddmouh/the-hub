using System.IO;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Native;

namespace TheHub.Core.Services;

public static class WallpaperService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv", ".gif"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp"
    };

    public static WallpaperType DetectWallpaperType(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        if (VideoExtensions.Contains(ext))
        {
            return WallpaperType.Animated;
        }
        return WallpaperType.Static;
    }

    public static string GetWallpapersStorageDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "TheHub", "Wallpapers");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public static string ImportWallpaperFile(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            throw new ArgumentException("Wallpaper source path cannot be empty.", nameof(sourceFilePath));

        string absoluteSource = Path.GetFullPath(sourceFilePath);
        if (!File.Exists(absoluteSource))
            throw new FileNotFoundException($"Wallpaper source file not found: {absoluteSource}", absoluteSource);

        string storageDir = GetWallpapersStorageDirectory();

        // If file is already in the storage directory, return full path directly
        string? currentDir = Path.GetDirectoryName(absoluteSource);
        if (string.Equals(currentDir, storageDir, StringComparison.OrdinalIgnoreCase))
        {
            return absoluteSource;
        }

        string fileName = Path.GetFileName(absoluteSource);
        string destPath = Path.Combine(storageDir, fileName);

        try
        {
            if (File.Exists(destPath))
            {
                var srcInfo = new FileInfo(absoluteSource);
                var dstInfo = new FileInfo(destPath);
                if (srcInfo.Length != dstInfo.Length)
                {
                    string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    destPath = Path.Combine(storageDir, $"{nameNoExt}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                    File.Copy(absoluteSource, destPath, true);
                }
            }
            else
            {
                File.Copy(absoluteSource, destPath, true);
            }

            string absoluteDest = Path.GetFullPath(destPath);
            AppLogger.Log($"[WallpaperService] Imported wallpaper from '{absoluteSource}' to '{absoluteDest}'");
            return absoluteDest;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[WallpaperService] ImportWallpaperFile copy failed ({ex.Message}), using source: '{absoluteSource}'");
            return absoluteSource;
        }
    }

    public static bool ApplyStaticWallpaper(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string fullPath = Path.GetFullPath(filePath);

            if (!File.Exists(fullPath))
            {
                AppLogger.Log($"ApplyStaticWallpaper failed: File not found: {fullPath}");
                return false;
            }

            bool ok = DesktopWorkerHelper.SetStaticWallpaper(fullPath);
            AppLogger.Log($"ApplyStaticWallpaper: {fullPath}, success={ok}");
            return ok;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"ApplyStaticWallpaper exception: {ex.Message}");
            return false;
        }
    }
}
