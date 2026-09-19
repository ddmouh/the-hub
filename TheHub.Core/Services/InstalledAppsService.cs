using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TheHub.Core.Services;

public class InstalledApp
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "⚡";
    public string Category { get; set; } = "General";
}

public static class InstalledAppsService
{
    private static List<InstalledApp>? _cachedApps;
    private static readonly object _lock = new();

    public static Task<List<InstalledApp>> GetInstalledApplicationsAsync(bool forceRefresh = false)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (_cachedApps != null && !forceRefresh)
                {
                    return _cachedApps;
                }

                var appsMap = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

                // 1. Scan Registry Uninstall keys (Standard Revo / Windows Add & Remove Programs)
                ScanRegistryUninstall(appsMap);

                // 2. Scan Windows Store / UWP / Modern Apps (Spotify, WhatsApp, etc.)
                ScanWindowsStoreApps(appsMap);

                // 3. Scan User Start Menu Shortcuts (.lnk)
                ScanStartMenu(appsMap);

                // 4. Add Essential Windows Utilities (Cleanly curated)
                AddCuratedSystemTools(appsMap);

                // 5. Final Filter & Sort
                var list = appsMap.Values
                    .Where(a => !IsIgnored(a.Name, a.ExecutablePath))
                    .OrderBy(a => a.Name)
                    .ToList();

                _cachedApps = list;
                return list;
            }
        });
    }

    /// <summary>
    /// Scans Registry Uninstall keys (HKLM, HKLM-Wow6432Node, HKCU).
    /// Matches the exact logic used by Windows "Installed apps" and Revo Uninstaller.
    /// </summary>
    private static void ScanRegistryUninstall(Dictionary<string, InstalledApp> map)
    {
        var targets = new (RegistryHive Hive, RegistryView View, string SubKey)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var (hive, view, subKeyPath) in targets)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
                if (uninstallKey == null) continue;

                foreach (var subName in uninstallKey.GetSubKeyNames())
                {
                    try
                    {
                        using var appKey = uninstallKey.OpenSubKey(subName);
                        if (appKey == null) continue;

                        string? rawDisplayName = appKey.GetValue("DisplayName")?.ToString()?.Trim();
                        if (string.IsNullOrWhiteSpace(rawDisplayName)) continue;

                        // Check SystemComponent - official Microsoft flag for hidden system drivers/updates
                        object? sysCompVal = appKey.GetValue("SystemComponent");
                        if (sysCompVal is int sysComp && sysComp == 1) continue;

                        // Check ParentKeyName - hides sub-features and patches
                        string? parentKey = appKey.GetValue("ParentKeyName")?.ToString();
                        if (!string.IsNullOrWhiteSpace(parentKey)) continue;

                        // Skip Windows Updates
                        string? releaseType = appKey.GetValue("ReleaseType")?.ToString();
                        if (!string.IsNullOrWhiteSpace(releaseType) &&
                            (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                             releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        if (IsIgnored(rawDisplayName, "")) continue;

                        string cleanName = CleanDisplayName(rawDisplayName);
                        if (string.IsNullOrWhiteSpace(cleanName) || IsIgnored(cleanName, "")) continue;

                        // Resolve executable path
                        string? execPath = ResolveRegistryExecutable(appKey, cleanName, subName);
                        if (string.IsNullOrWhiteSpace(execPath)) continue;

                        if (!map.ContainsKey(cleanName))
                        {
                            map[cleanName] = new InstalledApp
                            {
                                Name = cleanName,
                                ExecutablePath = execPath,
                                IconGlyph = InferIcon(cleanName, execPath),
                                Category = InferCategory(cleanName)
                            };
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static string? ResolveRegistryExecutable(RegistryKey appKey, string appName, string subKeyName)
    {
        // 1. Steam App ID check (e.g. Steam games)
        string? uninstallString = appKey.GetValue("UninstallString")?.ToString();
        if ((!string.IsNullOrEmpty(uninstallString) && uninstallString.Contains("steam://uninstall/", StringComparison.OrdinalIgnoreCase)) ||
            subKeyName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase))
        {
            string appId = subKeyName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase)
                ? subKeyName.Replace("Steam App ", "").Trim()
                : Regex.Match(uninstallString ?? "", @"\d+").Value;

            if (!string.IsNullOrEmpty(appId))
            {
                return $"steam://rungameid/{appId}";
            }
        }

        // 2. DisplayIcon parsing
        string? displayIcon = appKey.GetValue("DisplayIcon")?.ToString();
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            string cleanIcon = displayIcon.Trim('\"', ' ');
            // Strip index like ,0 or ,-1
            int commaIdx = cleanIcon.LastIndexOf(',');
            if (commaIdx > 0 && commaIdx > cleanIcon.LastIndexOf('.'))
            {
                cleanIcon = cleanIcon[..commaIdx].Trim('\"', ' ');
            }

            if (cleanIcon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !IsUninstallerExe(cleanIcon) &&
                File.Exists(cleanIcon))
            {
                return cleanIcon;
            }
        }

        // 3. InstallLocation scanning
        string? installLoc = appKey.GetValue("InstallLocation")?.ToString()?.Trim('\"', ' ');
        if (!string.IsNullOrWhiteSpace(installLoc) && Directory.Exists(installLoc))
        {
            try
            {
                var files = Directory.GetFiles(installLoc, "*.exe", SearchOption.TopDirectoryOnly);
                // First try finding an exe matching app name
                string appSimplified = Regex.Replace(appName, @"\s+", "").ToLowerInvariant();
                foreach (var f in files)
                {
                    string fName = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (!IsUninstallerExe(f) && (fName == appSimplified || appSimplified.Contains(fName) || fName.Contains(appSimplified)))
                    {
                        return f;
                    }
                }

                // Otherwise pick first non-uninstaller exe
                foreach (var f in files)
                {
                    if (!IsUninstallerExe(f)) return f;
                }
            }
            catch { }
        }

        // 4. Well-known app resolution
        var (knownExec, _) = AppLauncherService.ResolveExecutable(appName, "");
        if (File.Exists(knownExec))
        {
            return knownExec;
        }

        return null;
    }

    /// <summary>
    /// Scans Windows Store / Modern apps (like Spotify, WhatsApp, etc.).
    /// </summary>
    private static void ScanWindowsStoreApps(Dictionary<string, InstalledApp> map)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // 1. Spotify Store App specifically
        string spotifyStoreExe = Path.Combine(localAppData, "Microsoft", "WindowsApps", "Spotify.exe");
        if (File.Exists(spotifyStoreExe))
        {
            map["Spotify"] = new InstalledApp
            {
                Name = "Spotify",
                ExecutablePath = spotifyStoreExe,
                IconGlyph = "🎵",
                Category = "Media"
            };
        }

        // 2. Enumerate shell:AppsFolder via COM
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                if (folder != null)
                {
                    foreach (dynamic item in folder.Items())
                    {
                        try
                        {
                            string name = (string)item.Name;
                            string path = (string)item.Path;

                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                            if (IsIgnored(name, path)) continue;

                            // Handle Store Apps
                            if (path.Contains("!") || path.Contains("_8wekyb3d8bbwe") || path.Contains("_zpdnekdrzrea0") || path.StartsWith("5319275A.WhatsAppDesktop"))
                            {
                                string cleanName = CleanDisplayName(name);
                                if (IsIgnored(cleanName, "")) continue;

                                if (!map.ContainsKey(cleanName))
                                {
                                    // Check if execution alias exists in WindowsApps
                                    string aliasExe = Path.Combine(localAppData, "Microsoft", "WindowsApps", $"{cleanName}.exe");
                                    string targetPath = File.Exists(aliasExe) ? aliasExe : $"shell:AppsFolder\\{path}";

                                    map[cleanName] = new InstalledApp
                                    {
                                        Name = cleanName,
                                        ExecutablePath = targetPath,
                                        IconGlyph = InferIcon(cleanName, targetPath),
                                        Category = InferCategory(cleanName)
                                    };
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Scans User & Common Start Menu shortcut files (.lnk).
    /// </summary>
    private static void ScanStartMenu(Dictionary<string, InstalledApp> map)
    {
        try
        {
            var startFolders = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs)),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms))
            };

            dynamic? shell = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    shell = Activator.CreateInstance(shellType);
                }
            }
            catch { }

            if (shell == null) return;

            foreach (var folder in startFolders)
            {
                if (!Directory.Exists(folder)) continue;

                var lnkFiles = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnk in lnkFiles)
                {
                    try
                    {
                        string lnkDir = Path.GetDirectoryName(lnk) ?? "";
                        if (IsIgnoredDirectory(lnkDir)) continue;

                        string appName = Path.GetFileNameWithoutExtension(lnk);
                        if (IsIgnored(appName, "")) continue;

                        string cleanName = CleanDisplayName(appName);
                        if (map.ContainsKey(cleanName)) continue;

                        dynamic sc = shell.CreateShortcut(lnk);
                        string target = sc.TargetPath;
                        string args = sc.Arguments ?? "";

                        if (!string.IsNullOrWhiteSpace(target) &&
                            target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(target) &&
                            !IsUninstallerExe(target))
                        {
                            // Skip system32 executables unless whitelisted
                            if (IsRawSystem32Exe(target)) continue;

                            map[cleanName] = new InstalledApp
                            {
                                Name = cleanName,
                                ExecutablePath = target,
                                Arguments = args,
                                IconGlyph = InferIcon(cleanName, target),
                                Category = InferCategory(cleanName)
                            };
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static void AddCuratedSystemTools(Dictionary<string, InstalledApp> map)
    {
        AddSystemTool(map, "Notepad", @"notepad.exe", "📝", "System");
        AddSystemTool(map, "Paint", @"mspaint.exe", "🎨", "Design");
        AddSystemTool(map, "Calculator", "calc.exe", "🔢", "System");
        AddSystemTool(map, "Windows Terminal", "wt.exe", "💻", "Dev");
        AddSystemTool(map, "File Explorer", @"explorer.exe", "📁", "System");
        AddSystemTool(map, "Task Manager", @"taskmgr.exe", "⚙️", "System");
        AddSystemTool(map, "Snipping Tool", @"SnippingTool.exe", "✂️", "System");
    }

    private static void AddSystemTool(Dictionary<string, InstalledApp> map, string name, string path, string icon, string category)
    {
        if (!map.ContainsKey(name))
        {
            map[name] = new InstalledApp
            {
                Name = name,
                ExecutablePath = path,
                IconGlyph = icon,
                Category = category
            };
        }
    }

    private static bool IsUninstallerExe(string path)
    {
        string fName = Path.GetFileName(path).ToLowerInvariant();
        return fName.Contains("unins") ||
               fName.Contains("uninstall") ||
               fName == "setup.exe" ||
               fName.Contains("crashpad") ||
               fName.Contains("crashreporter") ||
               fName.Contains("diagnostics");
    }

    private static bool IsRawSystem32Exe(string path)
    {
        string lower = path.ToLowerInvariant();
        if (lower.Contains(@"\windows\system32\") || lower.Contains(@"\windows\syswow64\"))
        {
            string fName = Path.GetFileName(lower);
            string[] whitelist = { "notepad.exe", "mspaint.exe", "calc.exe", "explorer.exe", "taskmgr.exe", "snippingtool.exe", "wt.exe" };
            return !whitelist.Contains(fName);
        }
        return false;
    }

    private static bool IsIgnoredDirectory(string dirPath)
    {
        string lower = dirPath.ToLowerInvariant();
        string[] ignoredDirs =
        {
            "administrative tools",
            "windows administrative tools",
            "windows powershell",
            "windows accessories",
            "windows system",
            "accessibility",
            "maintenance",
            "startup"
        };

        foreach (var dir in ignoredDirs)
        {
            if (lower.Contains(dir)) return true;
        }
        return false;
    }

    private static bool IsIgnored(string name, string path)
    {
        string lowerName = name.ToLowerInvariant();
        string lowerPath = path.ToLowerInvariant();

        // Critical: Never allow spotify_cli or developer CLIs
        if (lowerName.Contains("spotify_cli") || lowerPath.Contains("spotify_cli") || lowerName.Contains("cli.exe"))
            return true;

        // Blacklist typical clutter, developer helpers, command prompts, and drivers
        string[] blacklistedWords =
        {
            "uninstall", "unins000", "setup", "helper", "updater", "crash",
            "diagnostics", "administrative tools", "application verifier", "native tools",
            "developer command prompt", "command prompt", "git bash", "git cmd", "node.js command prompt",
            "install additional tools", "odbc data sources", "event viewer", "performance monitor",
            "resource monitor", "task scheduler", "services", "system configuration",
            "system information", "print management", "windows memory diagnostic", "recovery drive",
            "windows app cert kit", "visual c++", "redistributable", ".net sdk", ".net runtime",
            "windows software development kit", "windows sdk", "realtek", "dolby audio",
            "epic online services", "vs_coreeditorfonts", "playstation(r) pc sdk", "update health tools",
            "gameinput", "package cache", "build tools", "language experience pack", "feedback hub",
            "get help", "windows backup", "windows web experience", "store experience host",
            "speech recognition", "documentation", "manual", "release notes", "what is new", "readme"
        };

        foreach (var word in blacklistedWords)
        {
            if (lowerName.Contains(word) || lowerPath.Contains(word))
            {
                // Preserve Discord (which has Update.exe as launcher)
                if (lowerName == "discord" || lowerName == "discord.exe") return false;
                return true;
            }
        }

        return false;
    }

    private static string CleanDisplayName(string name)
    {
        string clean = name.Trim();

        // Strip " (User)" suffix (e.g. "Antigravity IDE (User)")
        clean = Regex.Replace(clean, @"\s+\(User\)$", "", RegexOptions.IgnoreCase);

        // Strip " version X.X.X" or " vX.X.X" (e.g. "Stremio version 5.0.25")
        clean = Regex.Replace(clean, @"\s+version\s+[\d\.]+", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s+v?[\d\.]+(\s*\(64-bit\)|\s*\(32-bit\))?$", "", RegexOptions.IgnoreCase);

        return clean.Trim();
    }

    public static string InferIcon(string name, string path)
    {
        string s = (name + " " + path).ToLowerInvariant();

        if (s.Contains("discord")) return "💬";
        if (s.Contains("spotify") || s.Contains("music") || s.Contains("itunes") || s.Contains("sound")) return "🎵";
        if (s.Contains("steam") || s.Contains("epic") || s.Contains("game") || s.Contains("play") || s.Contains("riot") || s.Contains("blizzard") || s.Contains("vrchat")) return "🎮";
        if (s.Contains("chrome") || s.Contains("brave") || s.Contains("firefox") || s.Contains("edge") || s.Contains("browser") || s.Contains("opera")) return "🌐";
        if (s.Contains("obs") || s.Contains("stream") || s.Contains("cam") || s.Contains("video") || s.Contains("vlc") || s.Contains("player") || s.Contains("stremio")) return "📹";
        if (s.Contains("code") || s.Contains("visual studio") || s.Contains("terminal") || s.Contains("powershell") || s.Contains("git") || s.Contains("bash") || s.Contains("antigravity")) return "💻";
        if (s.Contains("note") || s.Contains("word") || s.Contains("excel") || s.Contains("office") || s.Contains("writer") || s.Contains("document")) return "📝";
        if (s.Contains("paint") || s.Contains("photoshop") || s.Contains("figma") || s.Contains("illustrator") || s.Contains("draw") || s.Contains("blender")) return "🎨";
        if (s.Contains("telegram") || s.Contains("whatsapp") || s.Contains("slack") || s.Contains("teams") || s.Contains("zoom") || s.Contains("chat")) return "💬";
        if (s.Contains("settings") || s.Contains("control") || s.Contains("task") || s.Contains("clean") || s.Contains("revo") || s.Contains("wiztree") || s.Contains("winrar") || s.Contains("7-zip")) return "⚙️";

        return "⚡";
    }

    public static string InferCategory(string name)
    {
        string s = name.ToLowerInvariant();

        if (s.Contains("discord") || s.Contains("telegram") || s.Contains("chat") || s.Contains("slack") || s.Contains("teams") || s.Contains("whatsapp")) return "Social";
        if (s.Contains("steam") || s.Contains("epic") || s.Contains("game") || s.Contains("riot") || s.Contains("vrchat") || s.Contains("play")) return "Gaming";
        if (s.Contains("chrome") || s.Contains("edge") || s.Contains("brave") || s.Contains("firefox") || s.Contains("browser")) return "Browsers";
        if (s.Contains("spotify") || s.Contains("obs") || s.Contains("music") || s.Contains("vlc") || s.Contains("player") || s.Contains("stremio")) return "Media";
        if (s.Contains("code") || s.Contains("terminal") || s.Contains("visual studio") || s.Contains("git") || s.Contains("antigravity") || s.Contains("ide")) return "Dev";
        if (s.Contains("paint") || s.Contains("figma") || s.Contains("photo") || s.Contains("draw") || s.Contains("blender")) return "Design";
        if (s.Contains("revo") || s.Contains("wiztree") || s.Contains("winrar") || s.Contains("7-zip") || s.Contains("cleaner") || s.Contains("tools")) return "Utilities";

        return "General";
    }
}
