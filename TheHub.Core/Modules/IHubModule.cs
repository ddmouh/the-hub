namespace TheHub.Core.Modules;

public enum ModuleState
{
    Stopped,
    Running,
    Suspended,
    Faulted
}

public enum SuspendReason
{
    Fullscreen,
    Battery,
    Workspace,
    User
}

public interface IHubModule : IDisposable
{
    string Id { get; }
    ModuleState State { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    void Suspend(SuspendReason reason);
    void Resume(SuspendReason reason);
}
