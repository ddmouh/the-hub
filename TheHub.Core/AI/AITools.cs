using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TheHub.Core.AI;

public interface IAITool
{
    string Name { get; }
    string Description { get; }
    AIPermissionLevel Level { get; }
    Task<AIToolExecutionResult> ExecuteAsync(Dictionary<string, string> args, CancellationToken ct = default);
}

public class ReadMetricsTool : IAITool
{
    public string Name => "read_metrics";
    public string Description => "Reads system resource metrics (CPU, RAM).";
    public AIPermissionLevel Level => AIPermissionLevel.Level0_Safe;

    public Task<AIToolExecutionResult> ExecuteAsync(Dictionary<string, string> args, CancellationToken ct = default)
    {
        // Safe read-only tool
        var mockCpu = 15.5;
        var mockRam = 42.0;
        return Task.FromResult(new AIToolExecutionResult(
            true,
            $"CPU: {mockCpu:F1}%, RAM: {mockRam:F1}% used",
            new { Cpu = mockCpu, Ram = mockRam }
        ));
    }
}

public class SetVolumeTool : IAITool
{
    private readonly Action<int>? _volumeSetter;

    public SetVolumeTool(Action<int>? volumeSetter = null)
    {
        _volumeSetter = volumeSetter;
    }

    public string Name => "set_volume";
    public string Description => "Sets the master volume level (0-100).";
    public AIPermissionLevel Level => AIPermissionLevel.Level1_LowRisk;

    public Task<AIToolExecutionResult> ExecuteAsync(Dictionary<string, string> args, CancellationToken ct = default)
    {
        if (args.TryGetValue("volume", out var volStr) && int.TryParse(volStr, out var vol))
        {
            var clamped = Math.Clamp(vol, 0, 100);
            _volumeSetter?.Invoke(clamped);
            return Task.FromResult(new AIToolExecutionResult(true, $"Volume set to {clamped}%", clamped));
        }

        return Task.FromResult(new AIToolExecutionResult(false, "Invalid or missing 'volume' argument"));
    }
}

public class SwitchWorkspaceTool : IAITool
{
    private readonly Func<string, Task<bool>>? _workspaceSwitcher;

    public SwitchWorkspaceTool(Func<string, Task<bool>>? workspaceSwitcher = null)
    {
        _workspaceSwitcher = workspaceSwitcher;
    }

    public string Name => "switch_workspace";
    public string Description => "Switches active workspace environment by name.";
    public AIPermissionLevel Level => AIPermissionLevel.Level1_LowRisk;

    public async Task<AIToolExecutionResult> ExecuteAsync(Dictionary<string, string> args, CancellationToken ct = default)
    {
        if (args.TryGetValue("workspace", out var wsName) && !string.IsNullOrWhiteSpace(wsName))
        {
            if (_workspaceSwitcher != null)
            {
                var ok = await _workspaceSwitcher(wsName);
                return new AIToolExecutionResult(ok, ok ? $"Switched to workspace '{wsName}'" : $"Workspace '{wsName}' switch failed");
            }
            return new AIToolExecutionResult(true, $"Switched to workspace '{wsName}'");
        }
        return new AIToolExecutionResult(false, "Missing 'workspace' argument");
    }
}

public class SetWallpaperTool : IAITool
{
    private readonly Action<string>? _wallpaperSetter;

    public SetWallpaperTool(Action<string>? wallpaperSetter = null)
    {
        _wallpaperSetter = wallpaperSetter;
    }

    public string Name => "set_wallpaper";
    public string Description => "Sets active wallpaper by path or preset name.";
    public AIPermissionLevel Level => AIPermissionLevel.Level1_LowRisk;

    public Task<AIToolExecutionResult> ExecuteAsync(Dictionary<string, string> args, CancellationToken ct = default)
    {
        if (args.TryGetValue("path", out var path) && !string.IsNullOrWhiteSpace(path))
        {
            _wallpaperSetter?.Invoke(path);
            return Task.FromResult(new AIToolExecutionResult(true, $"Wallpaper set to '{path}'"));
        }
        return Task.FromResult(new AIToolExecutionResult(false, "Missing 'path' argument"));
    }
}

public class LaunchAppTool : IAITool
{
    private readonly Action<string>? _appLauncher;

    public LaunchAppTool(Action<string>? appLauncher = null)
    {
        _appLauncher = appLauncher;
    }

    public string Name => "launch_app";
    public string Description => "Launches an application executable or shortcut.";
    public AIPermissionLevel Level => AIPermissionLevel.Level2_Elevated;

    public Task<AIToolExecutionResult> ExecuteAsync(Dictionary<string, string> args, CancellationToken ct = default)
    {
        if (args.TryGetValue("app", out var appName) && !string.IsNullOrWhiteSpace(appName))
        {
            _appLauncher?.Invoke(appName);
            return Task.FromResult(new AIToolExecutionResult(true, $"Application '{appName}' launched"));
        }
        return Task.FromResult(new AIToolExecutionResult(false, "Missing 'app' argument"));
    }
}

public class CloseAppTool : IAITool
{
    private readonly Action<string>? _appCloser;

    public CloseAppTool(Action<string>? appCloser = null)
    {
        _appCloser = appCloser;
    }

    public string Name => "close_app";
    public string Description => "Closes a running application process by name.";
    public AIPermissionLevel Level => AIPermissionLevel.Level2_Elevated;

    public Task<AIToolExecutionResult> ExecuteAsync(Dictionary<string, string> args, CancellationToken ct = default)
    {
        if (args.TryGetValue("process", out var procName) && !string.IsNullOrWhiteSpace(procName))
        {
            _appCloser?.Invoke(procName);
            return Task.FromResult(new AIToolExecutionResult(true, $"Application '{procName}' terminated"));
        }
        return Task.FromResult(new AIToolExecutionResult(false, "Missing 'process' argument"));
    }
}

public class ExecuteSystemScriptTool : IAITool
{
    public string Name => "execute_system_script";
    public string Description => "Executes arbitrary shell scripts or system modification commands.";
    public AIPermissionLevel Level => AIPermissionLevel.Level3_Dangerous;

    public Task<AIToolExecutionResult> ExecuteAsync(Dictionary<string, string> args, CancellationToken ct = default)
    {
        // By architecture policy, Level 3 is strictly blocked by default.
        return Task.FromResult(new AIToolExecutionResult(false, "Security Policy: Level 3 Dangerous system commands are permanently disabled."));
    }
}
