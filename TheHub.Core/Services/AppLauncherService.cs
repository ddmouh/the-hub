using System.Diagnostics;
using System.IO;
using TheHub.Common.Models;

namespace TheHub.Core.Services;

public class AppLauncherService
{
    public static bool LaunchShortcut(AppShortcut shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut.ExecutablePath))
            return false;

        _ = LaunchPathAsync(shortcut.ExecutablePath, shortcut.Arguments);
        return true;
    }

    public static bool LaunchPath(string path, string arguments = "")
    {
        _ = LaunchPathAsync(path, arguments);
        return true;
    }

    /// <summary>
    /// Checks whether an executable path exists on disk or can be resolved via system PATH or standard folders.
    /// </summary>
    public static bool IsPathResolvable(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return true;

        if (File.Exists(path) || Directory.Exists(path)) return true;

        var (resolvedPath, _) = ResolveExecutable(path, "");
        if (File.Exists(resolvedPath)) return true;

        // Check if executable is in system PATH
        return ExistsInPath(path);
    }

    /// <summary>
    /// Smart resolution of popular application executables if user only specifies the executable name (e.g. discord.exe, spotify.exe).
    /// </summary>
    public static (string Executable, string Arguments) ResolveExecutable(string path, string arguments)
    {
        string trimmed = path.Trim('\"', ' ');
        if (trimmed.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
        {
            return ("explorer.exe", trimmed);
        }

        if (trimmed.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return (trimmed, arguments);
        }

        if (File.Exists(trimmed))
        {
            return (trimmed, arguments);
        }

        string fileName = Path.GetFileName(trimmed).ToLowerInvariant();

        // 1. Discord resolution
        if (fileName == "discord.exe" || fileName == "discord")
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string discordUpdate = Path.Combine(localAppData, "Discord", "Update.exe");
            if (File.Exists(discordUpdate))
            {
                return (discordUpdate, "--processStart Discord.exe");
            }
            string discordAppDir = Path.Combine(localAppData, "Discord");
            if (Directory.Exists(discordAppDir))
            {
                var appDirs = Directory.GetDirectories(discordAppDir, "app-*");
                if (appDirs.Length > 0)
                {
                    Array.Sort(appDirs);
                    string latestApp = Path.Combine(appDirs[^1], "Discord.exe");
                    if (File.Exists(latestApp)) return (latestApp, arguments);
                }
            }
        }

        // 2. Spotify resolution
        if (fileName == "spotify.exe" || fileName == "spotify")
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string uwpSpotify = Path.Combine(localAppData, "Microsoft", "WindowsApps", "Spotify.exe");
            if (File.Exists(uwpSpotify)) return (uwpSpotify, arguments);

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string spotifyPath = Path.Combine(appData, "Spotify", "Spotify.exe");
            if (File.Exists(spotifyPath)) return (spotifyPath, arguments);
        }

        // 3. Steam resolution
        if (fileName == "steam.exe" || fileName == "steam")
        {
            string steam86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe");
            if (File.Exists(steam86)) return (steam86, arguments);
            string steam64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe");
            if (File.Exists(steam64)) return (steam64, arguments);
        }

        // 4. Chrome resolution
        if (fileName == "chrome.exe" || fileName == "chrome")
        {
            string chrome64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(chrome64)) return (chrome64, arguments);
            string chrome86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(chrome86)) return (chrome86, arguments);
        }

        // 5. VS Code resolution
        if (fileName == "code.exe" || fileName == "code")
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string codePath = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");
            if (File.Exists(codePath)) return (codePath, arguments);
        }

        // Standard PATH check
        string? pathExecutable = FindInPath(trimmed);
        if (pathExecutable != null)
        {
            return (pathExecutable, arguments);
        }

        return (path, arguments);
    }

    private static bool ExistsInPath(string fileName)
    {
        return FindInPath(fileName) != null;
    }

    private static string? FindInPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var extensions = new[] { "", ".exe", ".cmd", ".bat" };
        var folders = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var folder in folders)
        {
            foreach (var ext in extensions)
            {
                try
                {
                    string fullPath = Path.Combine(folder.Trim(), fileName + (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? "" : ext));
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch { }
            }
        }
        return null;
    }

    public static Task<(bool Success, string Message)> LaunchShortcutWithDetailsAsync(AppShortcut shortcut)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(shortcut.ExecutablePath))
                return (false, "Path is empty.");

            var (execPath, args) = ResolveExecutable(shortcut.ExecutablePath, shortcut.Arguments);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = execPath,
                    Arguments = args,
                    UseShellExecute = true
                };

                Process.Start(psi);
                return (true, $"Launched '{shortcut.Title}' successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to launch '{shortcut.Title}': {ex.Message}");
            }
        });
    }

    public static Task<bool> LaunchPathAsync(string path, string arguments = "")
    {
        return Task.Run(() =>
        {
            try
            {
                var (execPath, args) = ResolveExecutable(path, arguments);
                var psi = new ProcessStartInfo
                {
                    FileName = execPath,
                    Arguments = args,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppLauncher] Failed to launch '{path}': {ex.Message}");
                return false;
            }
        });
    }

    public static async Task<(int SuccessCount, int FailedCount, List<string> Messages)> ApplyWorkspaceDetailedAsync(WorkspaceConfig workspace)
    {
        int successCount = 0;
        int failedCount = 0;
        var messages = new List<string>();

        foreach (var app in workspace.Apps)
        {
            var (success, msg) = await LaunchShortcutWithDetailsAsync(app);
            if (success)
            {
                successCount++;
            }
            else
            {
                failedCount++;
                messages.Add(msg);
            }
        }

        workspace.IsActive = true;
        return (successCount, failedCount, messages);
    }

    public static async Task ApplyWorkspaceAsync(WorkspaceConfig workspace)
    {
        await ApplyWorkspaceDetailedAsync(workspace);
    }
}
