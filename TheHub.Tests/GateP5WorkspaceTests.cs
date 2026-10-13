using TheHub.Common.Models;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using TheHub.Core.Services;
using Xunit;

namespace TheHub.Tests;

public class GateP5WorkspaceTests
{
    [Fact]
    public void WorkspaceEngine_BuildExecutionPlan_ComputesLaunchesCorrectly()
    {
        var target = new WorkspaceConfig
        {
            Name = "Coding",
            Apps = new List<AppShortcut>
            {
                new() { Title = "VS Code", ExecutablePath = @"C:\Tools\Code.exe" },
                new() { Title = "Terminal", ExecutablePath = @"C:\Tools\wt.exe" }
            },
            TargetVolume = 40,
            WallpaperId = "wp-dev-dark",
            RadialProfileId = "dev-wheel"
        };

        var plan = WorkspaceEngine.BuildExecutionPlan(target);

        Assert.NotNull(plan);
        Assert.Equal(target, plan.TargetWorkspace);

        // Should contain 2 LaunchApp actions, 1 SetVolume, 1 SetWallpaper, 1 SetRadialProfile
        Assert.Contains(plan.Actions, a => a.Type == WorkspaceActionType.LaunchApp && a.Target == @"C:\Tools\Code.exe");
        Assert.Contains(plan.Actions, a => a.Type == WorkspaceActionType.LaunchApp && a.Target == @"C:\Tools\wt.exe");
        Assert.Contains(plan.Actions, a => a.Type == WorkspaceActionType.SetVolume && a.Target == "40");
        Assert.Contains(plan.Actions, a => a.Type == WorkspaceActionType.SetWallpaper && a.Target == "wp-dev-dark");
        Assert.Contains(plan.Actions, a => a.Type == WorkspaceActionType.SetRadialProfile && a.Target == "dev-wheel");
    }

    [Fact]
    public void WorkspaceEngine_KillOthersMode_SchedulesPreviousAppsForClosure()
    {
        var current = new WorkspaceConfig
        {
            Name = "Gaming",
            Apps = new List<AppShortcut>
            {
                new() { Title = "Game Client", ExecutablePath = @"C:\Games\Steam.exe" },
                new() { Title = "Discord", ExecutablePath = @"C:\Tools\Discord.exe" }
            }
        };

        var target = new WorkspaceConfig
        {
            Name = "Focus",
            SwitchMode = WorkspaceSwitchMode.KillOthers,
            Apps = new List<AppShortcut>
            {
                new() { Title = "Discord", ExecutablePath = @"C:\Tools\Discord.exe" },
                new() { Title = "Obsidian", ExecutablePath = @"C:\Notes\Obsidian.exe" }
            }
        };

        var runningProcesses = new List<ProcessSnapshot>
        {
            new(1001, "Steam", "Steam Client", @"C:\Games\Steam.exe"),
            new(1002, "Discord", "Discord", @"C:\Tools\Discord.exe")
        };

        var plan = WorkspaceEngine.BuildExecutionPlan(target, current, runningProcesses);

        // Steam should be scheduled to close (in current, not in target)
        Assert.Contains(plan.Actions, a => a.Type == WorkspaceActionType.CloseApp && a.Target == "Steam");

        // Discord should NOT be closed (it is present in target)
        Assert.DoesNotContain(plan.Actions, a => a.Type == WorkspaceActionType.CloseApp && a.Target == "Discord");

        // Obsidian should be launched
        Assert.Contains(plan.Actions, a => a.Type == WorkspaceActionType.LaunchApp && a.Target == @"C:\Notes\Obsidian.exe");
    }

    [Fact]
    public void WorkspaceEngine_ProcessSnapshotFilter_ExcludesSystemProcesses()
    {
        Assert.False(WorkspaceEngine.IsUserApplication("explorer", "Windows Explorer"));
        Assert.False(WorkspaceEngine.IsUserApplication("dwm", "Desktop Window Manager"));
        Assert.False(WorkspaceEngine.IsUserApplication("svchost", "Service Host"));
        Assert.False(WorkspaceEngine.IsUserApplication("thehub", "The Hub"));
        Assert.False(WorkspaceEngine.IsUserApplication("", "Empty Process"));
        Assert.False(WorkspaceEngine.IsUserApplication("notepad", "")); // No window title

        Assert.True(WorkspaceEngine.IsUserApplication("Code", "Visual Studio Code"));
        Assert.True(WorkspaceEngine.IsUserApplication("chrome", "Google Chrome"));
        Assert.True(WorkspaceEngine.IsUserApplication("spotify", "Spotify Free"));
    }

    [Fact]
    public void WorkspaceEngine_CreateWorkspaceFromSnapshot_ProducesValidConfig()
    {
        var snapshots = new List<ProcessSnapshot>
        {
            new(2001, "Code", "workspace - VS Code", @"C:\AppData\Code.exe"),
            new(2002, "chrome", "GitHub - Chrome", @"C:\Program Files\chrome.exe")
        };

        var ws = WorkspaceEngine.CreateWorkspaceFromSnapshot("Dev Snapshot", "💻", "#3B82F6", snapshots);

        Assert.Equal("Dev Snapshot", ws.Name);
        Assert.Equal("💻", ws.IconGlyph);
        Assert.Equal("#3B82F6", ws.AccentColor);
        Assert.Equal(2, ws.Apps.Count);
        Assert.Equal("workspace - VS Code", ws.Apps[0].Title);
        Assert.Equal(@"C:\AppData\Code.exe", ws.Apps[0].ExecutablePath);
    }

    [Fact]
    public async Task WorkspaceModule_Lifecycle_StartsAndStops()
    {
        var bus = new HubBus();
        var bridge = new UiBridge();
        using var module = new WorkspaceModule(bus, bridge);

        Assert.Equal(ModuleState.Stopped, module.State);

        await module.StartAsync();
        Assert.Equal(ModuleState.Running, module.State);

        await module.StopAsync();
        Assert.Equal(ModuleState.Stopped, module.State);
    }

    [Fact]
    public async Task WorkspaceModule_SwitchToWorkspace_PublishesWorkspaceChangedEvent()
    {
        var bus = new HubBus();
        var bridge = new UiBridge();
        using var module = new WorkspaceModule(bus, bridge);
        await module.StartAsync();

        WorkspaceChanged? received = null;
        using var sub = bus.Subscribe<WorkspaceChanged>(evt => received = evt);

        var target = new WorkspaceConfig
        {
            Name = "Creative Studio",
            AccentColor = "#EC4899",
            TargetVolume = 65
        };

        bool ok = await module.SwitchToWorkspaceAsync(target);
        await Task.Delay(50); // allow HubBus to dispatch

        Assert.True(ok);
        Assert.Equal(target, module.ActiveWorkspace);
        Assert.NotNull(received);
        Assert.Equal("Creative Studio", received.WorkspaceName);
        Assert.Equal("#EC4899", received.AccentColor);
    }
}
