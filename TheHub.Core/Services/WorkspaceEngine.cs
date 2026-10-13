using System.Diagnostics;
using System.IO;
using TheHub.Common.Models;

namespace TheHub.Core.Services;

/// <summary>
/// Pure execution engine for desktop workspaces.
/// Computes atomic transition plans between workspaces and captures desktop snapshots.
/// </summary>
public static class WorkspaceEngine
{
    private static readonly HashSet<string> SystemProcessBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "explorer", "taskmgr", "shellexperiencehost", "applicationframehost",
        "system", "idle", "svchost", "smss", "csrss", "wininit", "services", "lsass",
        "winlogon", "fontdrvhost", "conhost", "runtimebroker", "sihost", "searchhost",
        "startmenuexperiencehost", "textinputhost", "ctfmon", "thehub", "lockapp"
    };

    /// <summary>
    /// Pure function computing the atomic execution plan from the current state to the target workspace.
    /// </summary>
    public static WorkspaceExecutionPlan BuildExecutionPlan(
        WorkspaceConfig targetWorkspace,
        WorkspaceConfig? currentWorkspace = null,
        IEnumerable<ProcessSnapshot>? runningProcesses = null)
    {
        if (targetWorkspace == null) throw new ArgumentNullException(nameof(targetWorkspace));

        var actions = new List<WorkspaceAction>();
        var runningList = runningProcesses?.ToList() ?? new List<ProcessSnapshot>();

        // 1. Process termination (if KillOthers or previous workspace requests close on exit)
        bool shouldClosePrevious = (targetWorkspace.SwitchMode == WorkspaceSwitchMode.KillOthers ||
                                    targetWorkspace.SwitchMode == WorkspaceSwitchMode.Isolate ||
                                    (currentWorkspace?.CloseAppsOnExit ?? false));

        if (shouldClosePrevious && currentWorkspace != null)
        {
            var targetExeNames = targetWorkspace.Apps
                .Select(a => Path.GetFileNameWithoutExtension(a.ExecutablePath))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var app in currentWorkspace.Apps)
            {
                string exeName = Path.GetFileNameWithoutExtension(app.ExecutablePath);
                if (!string.IsNullOrWhiteSpace(exeName) && !targetExeNames.Contains(exeName))
                {
                    // Check if running
                    var match = runningList.FirstOrDefault(p =>
                        string.Equals(p.ProcessName, exeName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        actions.Add(new WorkspaceAction(
                            WorkspaceActionType.CloseApp,
                            match.ProcessName,
                            match.ProcessId.ToString()));
                    }
                }
            }
        }

        // 2. Apps to launch
        foreach (var app in targetWorkspace.Apps)
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath)) continue;

            string exeName = Path.GetFileNameWithoutExtension(app.ExecutablePath);
            bool isAlreadyRunning = runningList.Any(p =>
                string.Equals(p.ProcessName, exeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase));

            // In Isolate mode or if not already running, schedule launch
            if (!isAlreadyRunning || targetWorkspace.SwitchMode == WorkspaceSwitchMode.LaunchOnly)
            {
                actions.Add(new WorkspaceAction(
                    WorkspaceActionType.LaunchApp,
                    app.ExecutablePath,
                    app.Arguments ?? "",
                    app.Title));
            }
        }

        // 3. Audio profile
        if (targetWorkspace.TargetVolume.HasValue)
        {
            actions.Add(new WorkspaceAction(
                WorkspaceActionType.SetVolume,
                targetWorkspace.TargetVolume.Value.ToString()));
        }

        // 4. Wallpaper link
        if (!string.IsNullOrWhiteSpace(targetWorkspace.WallpaperId))
        {
            actions.Add(new WorkspaceAction(
                WorkspaceActionType.SetWallpaper,
                targetWorkspace.WallpaperId));
        }

        // 5. Radial profile link
        if (!string.IsNullOrWhiteSpace(targetWorkspace.RadialProfileId))
        {
            actions.Add(new WorkspaceAction(
                WorkspaceActionType.SetRadialProfile,
                targetWorkspace.RadialProfileId));
        }

        // 6. Do Not Disturb
        if (targetWorkspace.EnableDoNotDisturb)
        {
            actions.Add(new WorkspaceAction(
                WorkspaceActionType.SetDoNotDisturb,
                "Enabled"));
        }

        return new WorkspaceExecutionPlan
        {
            TargetWorkspace = targetWorkspace,
            PreviousWorkspace = currentWorkspace,
            Actions = actions,
            EstimatedDurationMs = actions.Count * 80 + 150
        };
    }

    /// <summary>
    /// Captures running user applications with interactive top-level windows,
    /// filtering out Windows core services and The Hub companion process.
    /// </summary>
    public static List<ProcessSnapshot> CaptureDesktopSnapshot()
    {
        var snapshots = new List<ProcessSnapshot>();
        string currentProcName = Process.GetCurrentProcess().ProcessName;

        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (proc.Id <= 4) continue;
                    if (proc.MainWindowHandle == IntPtr.Zero) continue;
                    if (string.IsNullOrWhiteSpace(proc.MainWindowTitle)) continue;

                    string name = proc.ProcessName;
                    if (SystemProcessBlacklist.Contains(name)) continue;
                    if (string.Equals(name, currentProcName, StringComparison.OrdinalIgnoreCase)) continue;

                    string exePath = "";
                    try
                    {
                        exePath = proc.MainModule?.FileName ?? "";
                    }
                    catch
                    {
                        // Some 32/64-bit protected processes throw Win32Exception on MainModule
                        exePath = name + ".exe";
                    }

                    snapshots.Add(new ProcessSnapshot(
                        proc.Id,
                        proc.ProcessName,
                        proc.MainWindowTitle,
                        exePath));
                }
                catch
                {
                    // Ignore transient process exits
                }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }
        }
        catch
        {
        }

        return snapshots;
    }

    /// <summary>
    /// Creates a new workspace configuration from a captured desktop snapshot.
    /// </summary>
    public static WorkspaceConfig CreateWorkspaceFromSnapshot(
        string name,
        string iconGlyph = "🖥️",
        string accentColor = "#10B981",
        IEnumerable<ProcessSnapshot>? snapshots = null)
    {
        var items = snapshots?.ToList() ?? CaptureDesktopSnapshot();
        var shortcuts = new List<AppShortcut>();

        int order = 1;
        foreach (var snap in items)
        {
            shortcuts.Add(new AppShortcut
            {
                Title = !string.IsNullOrWhiteSpace(snap.MainWindowTitle) ? snap.MainWindowTitle : snap.ProcessName,
                ExecutablePath = snap.ExecutablePath,
                Order = order++
            });
        }

        return new WorkspaceConfig
        {
            Name = name,
            IconGlyph = iconGlyph,
            AccentColor = accentColor,
            Apps = shortcuts,
            Description = $"Captured snapshot with {shortcuts.Count} application(s)"
        };
    }

    /// <summary>
    /// Pure filter tester for process snapshots.
    /// </summary>
    public static bool IsUserApplication(string processName, string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        if (string.IsNullOrWhiteSpace(windowTitle)) return false;
        if (SystemProcessBlacklist.Contains(processName)) return false;
        return true;
    }
}
