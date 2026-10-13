using System.Collections.Concurrent;

namespace TheHub.Core.Modules;

public class ModuleHost : IDisposable
{
    private class ModuleRecord
    {
        public required IHubModule Module { get; init; }
        public int FailureCount { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    private readonly ConcurrentDictionary<string, ModuleRecord> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string, Exception>? _logHandler;
    private bool _isDisposed;

    public ModuleHost(Action<string, Exception>? logHandler = null)
    {
        _logHandler = logHandler;
    }

    public void Register(IHubModule module, bool isEnabled = true)
    {
        var record = new ModuleRecord
        {
            Module = module,
            IsEnabled = isEnabled,
            FailureCount = 0
        };

        _modules[module.Id] = record;
    }

    public IHubModule? GetModule(string id)
    {
        return _modules.TryGetValue(id, out var record) ? record.Module : null;
    }

    public IReadOnlyCollection<IHubModule> GetAllModules()
    {
        return _modules.Values.Select(r => r.Module).ToList();
    }

    public async Task StartAllAsync(CancellationToken ct = default)
    {
        foreach (var record in _modules.Values)
        {
            if (record.IsEnabled && record.Module.State != ModuleState.Running)
            {
                await StartModuleInternalAsync(record, ct).ConfigureAwait(false);
            }
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var record in _modules.Values)
        {
            if (record.Module.State == ModuleState.Running || record.Module.State == ModuleState.Suspended)
            {
                try
                {
                    await record.Module.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logHandler?.Invoke($"Error stopping module '{record.Module.Id}'", ex);
                }
            }
        }
    }

    public async Task SetModuleEnabledAsync(string id, bool enabled)
    {
        if (!_modules.TryGetValue(id, out var record)) return;

        record.IsEnabled = enabled;
        if (enabled)
        {
            record.FailureCount = 0; // Reset failures on manual enable
            await StartModuleInternalAsync(record).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await record.Module.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logHandler?.Invoke($"Error stopping module '{record.Module.Id}'", ex);
            }
        }
    }

    public async Task HandleModuleFailureAsync(string id, Exception ex)
    {
        if (!_modules.TryGetValue(id, out var record)) return;

        record.FailureCount++;
        _logHandler?.Invoke($"Module '{id}' reported failure (Attempt {record.FailureCount}/3)", ex);

        if (record.FailureCount <= 3)
        {
            try
            {
                await record.Module.StopAsync().ConfigureAwait(false);
            }
            catch { }

            await StartModuleInternalAsync(record).ConfigureAwait(false);
        }
        else
        {
            _logHandler?.Invoke($"Module '{id}' has faulted 3 times. Marking as Faulted.", ex);
        }
    }

    private async Task StartModuleInternalAsync(ModuleRecord record, CancellationToken ct = default)
    {
        try
        {
            await record.Module.StartAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await HandleModuleFailureAsync(record.Module.Id, ex).ConfigureAwait(false);
        }
    }

    public void SuspendAll(SuspendReason reason)
    {
        foreach (var record in _modules.Values)
        {
            if (record.IsEnabled && record.Module.State == ModuleState.Running)
            {
                try
                {
                    record.Module.Suspend(reason);
                }
                catch (Exception ex)
                {
                    _logHandler?.Invoke($"Error suspending module '{record.Module.Id}'", ex);
                }
            }
        }
    }

    public void ResumeAll(SuspendReason reason)
    {
        foreach (var record in _modules.Values)
        {
            if (record.IsEnabled && record.Module.State == ModuleState.Suspended)
            {
                try
                {
                    record.Module.Resume(reason);
                }
                catch (Exception ex)
                {
                    _logHandler?.Invoke($"Error resuming module '{record.Module.Id}'", ex);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        foreach (var record in _modules.Values)
        {
            try
            {
                record.Module.Dispose();
            }
            catch { }
        }
        _modules.Clear();
    }
}
