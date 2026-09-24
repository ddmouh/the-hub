using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TheHub.Common.Config;

/// <summary>
/// Runtime-installable custom font descriptor.
/// PreviewFontFamily is a URI-based FontFamily so WPF can render
/// file-installed fonts (not registered with Windows) directly.
/// </summary>
public sealed class CustomFontInfo
{
    public string FileName { get; init; } = string.Empty;
    public string FamilyName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public FontFamily PreviewFontFamily { get; init; } = new FontFamily("Segoe UI");
}

/// <summary>
/// Manages user-installed external fonts (.ttf / .otf) stored under
/// %AppData%\TheHub\Fonts and resolves them to WPF FontFamily instances
/// for the Clock Widget, Dynamic Island, and general app typography.
/// MVVM-friendly: no UI dependencies, pure install/scan/resolve API.
/// </summary>
public static class CustomFontService
{
    public static readonly string FontsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TheHub",
        "Fonts"
    );

    private static readonly string[] SupportedExtensions = { ".ttf", ".otf" };
    private static readonly object SyncLock = new();

    static CustomFontService()
    {
        EnsureFontsDirectory();
    }

    public static void EnsureFontsDirectory()
    {
        try
        {
            if (!Directory.Exists(FontsDirectory))
            {
                Directory.CreateDirectory(FontsDirectory);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"CustomFontService.EnsureFontsDirectory error: {ex.Message}");
        }
    }

    public static bool IsSupportedFontFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = Path.GetExtension(path);
        return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans the fonts directory and returns installed fonts with their
    /// real internal family names (via GlyphTypeface) and URI-based preview families.
    /// </summary>
    public static List<CustomFontInfo> GetInstalledFonts()
    {
        var result = new List<CustomFontInfo>();
        lock (SyncLock)
        {
            try
            {
                EnsureFontsDirectory();
                var files = Directory.EnumerateFiles(FontsDirectory)
                    .Where(IsSupportedFontFile)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

                foreach (string file in files)
                {
                    var info = BuildFontInfo(file);
                    if (info != null) result.Add(info);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"CustomFontService.GetInstalledFonts error: {ex.Message}");
            }
        }
        return result;
    }

    public static List<string> GetInstalledFamilyNames()
    {
        return GetInstalledFonts().Select(f => f.FamilyName).ToList();
    }

    /// <summary>
    /// Copies a user-selected .ttf/.otf file into the fonts directory.
    /// Returns the installed font info, or null on failure.
    /// </summary>
    public static CustomFontInfo? InstallFont(string sourcePath)
    {
        lock (SyncLock)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    return null;
                }
                if (!IsSupportedFontFile(sourcePath))
                {
                    return null;
                }

                EnsureFontsDirectory();
                string destPath = Path.Combine(FontsDirectory, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destPath, overwrite: true);
                AppLogger.Log($"CustomFontService: Installed font '{destPath}'");
                return BuildFontInfo(destPath);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"CustomFontService.InstallFont error: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Deletes an installed font by file name (directory traversal safe).
    /// </summary>
    public static bool DeleteFont(string fileName)
    {
        lock (SyncLock)
        {
            try
            {
                string safeName = Path.GetFileName(fileName);
                if (string.IsNullOrWhiteSpace(safeName)) return false;
                string target = Path.Combine(FontsDirectory, safeName);
                if (!File.Exists(target)) return false;
                File.Delete(target);
                AppLogger.Log($"CustomFontService: Deleted font '{target}'");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"CustomFontService.DeleteFont error: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Resolves a font display name to a usable WPF FontFamily.
    /// Custom installed fonts resolve via file URI; anything else falls
    /// back to a system FontFamily (with message-font fallback on failure).
    /// </summary>
    public static FontFamily ResolveFontFamily(string? displayName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return new FontFamily("Consolas");
            }

            var installed = GetInstalledFonts().FirstOrDefault(f =>
                string.Equals(f.FamilyName, displayName, StringComparison.OrdinalIgnoreCase));
            if (installed != null)
            {
                return installed.PreviewFontFamily;
            }

            return new FontFamily(displayName);
        }
        catch
        {
            return SystemFonts.MessageFontFamily;
        }
    }

    private static CustomFontInfo? BuildFontInfo(string filePath)
    {
        try
        {
            string fileName = Path.GetFileName(filePath);
            string fallbackName = Path.GetFileNameWithoutExtension(filePath);
            string familyName = fallbackName;

            // Read the real internal family name so the URI " ./#Name " matches.
            try
            {
                var glyph = new GlyphTypeface(new Uri(filePath, UriKind.Absolute));
                if (glyph.FamilyNames.TryGetValue(new CultureInfo("en-US"), out string? enName) &&
                    !string.IsNullOrWhiteSpace(enName))
                {
                    familyName = enName;
                }
                else
                {
                    string? first = glyph.FamilyNames.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                    if (!string.IsNullOrWhiteSpace(first)) familyName = first;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"CustomFontService.BuildFontInfo glyph read fallback for '{fileName}': {ex.Message}");
            }

            FontFamily preview;
            try
            {
                var dirUri = new Uri(FontsDirectory + Path.DirectorySeparatorChar, UriKind.Absolute);
                preview = new FontFamily(dirUri, "./#" + familyName);
            }
            catch
            {
                preview = new FontFamily(fallbackName);
            }

            return new CustomFontInfo
            {
                FileName = fileName,
                FamilyName = familyName,
                FilePath = filePath,
                PreviewFontFamily = preview
            };
        }
        catch (Exception ex)
        {
            AppLogger.Log($"CustomFontService.BuildFontInfo error: {ex.Message}");
            return null;
        }
    }
}
