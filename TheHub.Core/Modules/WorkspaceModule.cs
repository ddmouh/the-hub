using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Services;

namespace TheHub.Core.Modules;

/// <summary>
/// Hub module responsible for desktop workspaces lifecycle, atomic switching,
/// and profile inheritance (wallpapers, audio, radial profiles).
/// </summary>
public sealed class WorkspaceModule : IHubModule
{
    private readonly IHubBus _bus;
    private readonly UiBridge _uiBridge;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _lock = new();
    private WorkspaceConfig? _activeWorkspace;
    private bool _isDisposed;

    public string Id => "workspaces";
    public ModuleState State { get; private set; } = ModuleState.Stopped;
    public WorkspaceConfig? ActiveWorkspace
    {
        get { lock (_lock) return _activeWorkspace; }
        private set { lock (_lock) _activeWorkspace = value; }
    }

    public WorkspaceModule(IHubBus bus, UiBridge uiBridge)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _uiBridge = uiBridge ?? throw new ArgumentNullException(nameof(uiBridge));
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (State == ModuleState.Running) return Task.CompletedTask;

        _subscriptions.Add(_bus.Subscribe<SettingsChanged>(OnSettingsChanged));

        // Locate initially active workspace from settings
        var settings = SettingsManager.Current;
        var initial = settings.Workspaces.FirstOrDefault(w => w.IsActive) ?? settings.Workspaces.FirstOrDefault();
        if (initial != null)
        {
            initial.IsActive = true;
            ActiveWorkspace = initial;
        }

        State = ModuleState.Running;
        AppLogger.Log($"[WorkspaceModule] Started with active workspace: '{ActiveWorkspace?.Name ?? "None"}'");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (State == ModuleState.Stopped) return Task.CompletedTask;

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();

        State = ModuleState.Stopped;
        AppLogger.Log("[WorkspaceModule] Stopped successfully.");
        return Task.CompletedTask;
    }

    public void Suspend(SuspendReason reason)
    {
        State = ModuleState.Suspended;
    }

    public void Resume(SuspendReason reason)
    {
        State = ModuleState.Running;
    }

    private void OnSettingsChanged(SettingsChanged evt)
    {
        if (evt.SectionName.Equals("workspaces", StringComparison.OrdinalIgnoreCase) ||
            evt.SectionName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var settings = SettingsManager.Current;
            var current = ActiveWorkspace;
            if (current != null)
            {
                var match = settings.Workspaces.FirstOrDefault(w => w.Id == current.Id);
                if (match != null)
                {
                    ActiveWorkspace = match;
                }
            }
        }
    }

    /// <summary>
    /// Executes an atomic asynchronous workspace transition.
    /// Never blocks the UI thread.
    /// </summary>
    public async Task<bool> SwitchToWorkspaceAsync(WorkspaceConfig target, CancellationToken ct = default)
    {
        if (target == null) return false;

        var previous = ActiveWorkspace;
        AppLogger.Log($"[WorkspaceModule] Transitioning from '{previous?.Name ?? "None"}' to '{target.Name}' (SwitchMode: {target.SwitchMode})...");

        try
        {
            // 1. Build pure execution plan off UI thread
            var runningSnapshots = await Task.Run(WorkspaceEngine.CaptureDesktopSnapshot, ct).ConfigureAwait(false);
            var plan = WorkspaceEngine.BuildExecutionPlan(target, previous, runningSnapshots);

            // 2. Execute plan actions sequentially off the UI thread
            foreach (var action in plan.Actions)
            {
                if (ct.IsCancellationRequested) break;

                switch (action.Type)
                {
                    case WorkspaceActionType.LaunchApp:
                        await AppLauncherService.LaunchPathAsync(action.Target, action.Arguments).ConfigureAwait(false);
                        break;

                    case WorkspaceActionType.CloseApp:
                        await CloseProcessByNameAsync(action.Target).ConfigureAwait(false);
                        break;

                    case WorkspaceActionType.SetVolume:
                        if (int.TryParse(action.Target, out int vol))
                        {
                            _bus.Publish(new VolumeChanged(vol, false));
                        }
                        break;

                    case WorkspaceActionType.SetDoNotDisturb:
                        // Log or trigger DND policy
                        AppLogger.Log("[WorkspaceModule] Do Not Disturb enabled for workspace.");
                        break;
                }
            }

            // 3. Update active workspace states atomically on UI thread
            await _uiBridge.InvokeAsync(() =>
            {
                var settings = SettingsManager.Current;
                foreach (var ws in settings.Workspaces)
                {
                    ws.IsActive = (ws.Id == target.Id);
                }
                target.IsActive = true;
                SettingsManager.SaveSettings();
            });

            ActiveWorkspace = target;

            // 4. Publish WorkspaceChanged event to HubBus
            _bus.Publish(new WorkspaceChanged(target.Name, target.AccentColor));

            AppLogger.Log($"[WorkspaceModule] Workspace '{target.Name}' activated successfully.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[WorkspaceModule] Error switching to workspace '{target.Name}': {ex.Message}");
            return false;
        }
    }

    private static Task CloseProcessByNameAsync(string processName)
    {
        return Task.Run(() =>
        {
            try
            {
                var procs = System.Diagnostics.Process.GetProcessesByName(processName);
                foreach (var p in procs)
                {
                    try
                    {
                        if (!p.CloseMainWindow())
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch { }
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _ = StopAsync();
    }
}
