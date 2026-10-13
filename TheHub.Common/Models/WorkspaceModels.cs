namespace TheHub.Common.Models;

public enum WorkspaceSwitchMode
{
    LaunchOnly,
    KillOthers,
    Isolate
}

public enum WorkspaceActionType
{
    LaunchApp,
    CloseApp,
    SetVolume,
    SetWallpaper,
    SetRadialProfile,
    SetDoNotDisturb
}

public record WorkspaceAction(
    WorkspaceActionType Type,
    string Target,
    string Arguments = "",
    string? Payload = null
);

public record ProcessSnapshot(
    int ProcessId,
    string ProcessName,
    string MainWindowTitle,
    string ExecutablePath
);

public class WorkspaceExecutionPlan
{
    public required WorkspaceConfig TargetWorkspace { get; init; }
    public WorkspaceConfig? PreviousWorkspace { get; init; }
    public List<WorkspaceAction> Actions { get; init; } = new();
    public int EstimatedDurationMs { get; init; } = 300;
}
