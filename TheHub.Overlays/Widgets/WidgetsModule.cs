using TheHub.Common.Config;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using TheHub.Core.Services;

namespace TheHub.Overlays.Widgets;

/// <summary>
/// Hub module managing the lifecycle, polling rates, and layout of desktop widgets.
/// Decouples all widgets from MainWindow and coordinates power policy suspension.
/// </summary>
public sealed class WidgetsModule : IHubModule
{
    private readonly IHubBus _bus;
    private readonly UiBridge _uiBridge;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly HashSet<SuspendReason> _activeSuspendReasons = new();
    private readonly object _lock = new();

    private DesktopClockWidget? _clockWidget;
    private DesktopCpuWidget? _cpuWidget;
    private DesktopGpuWidget? _gpuWidget;
    private DesktopRamWidget? _ramWidget;
    private bool _isDisposed;

    public string Id => "widgets";
    public ModuleState State { get; private set; } = ModuleState.Stopped;

    public DesktopClockWidget? ClockWidget => _clockWidget;
    public DesktopCpuWidget? CpuWidget => _cpuWidget;
    public DesktopGpuWidget? GpuWidget => _gpuWidget;
    public DesktopRamWidget? RamWidget => _ramWidget;

    public bool IsPaused
    {
        get
        {
            lock (_lock) return _activeSuspendReasons.Count > 0;
        }
    }

    public WidgetsModule(IHubBus bus, UiBridge uiBridge)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _uiBridge = uiBridge ?? throw new ArgumentNullException(nameof(uiBridge));
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (State == ModuleState.Running) return;

        _subscriptions.Add(_bus.Subscribe<FullscreenChanged>(OnFullscreenChanged));
        _subscriptions.Add(_bus.Subscribe<PowerStateChanged>(OnPowerStateChanged));
        _subscriptions.Add(_bus.Subscribe<SettingsChanged>(OnSettingsChanged));

        await _uiBridge.InvokeAsync(() =>
        {
            EnsureWidgetsCreated();
            ApplySettingsToWidgets();
        });

        State = ModuleState.Running;
        AppLogger.Log("[WidgetsModule] Started successfully.");
    }

    public async Task StopAsync()
    {
        if (State == ModuleState.Stopped) return;

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();

        await _uiBridge.InvokeAsync(() =>
        {
            CloseAllWidgets();
        });

        lock (_lock)
        {
            _activeSuspendReasons.Clear();
        }

        State = ModuleState.Stopped;
        AppLogger.Log("[WidgetsModule] Stopped successfully.");
    }

    public void Suspend(SuspendReason reason)
    {
        lock (_lock)
        {
            _activeSuspendReasons.Add(reason);
        }

        _uiBridge.Post(() =>
        {
            PauseAllPolling();
            HideAllWidgets();
        });

        State = ModuleState.Suspended;
        AppLogger.Log($"[WidgetsModule] Suspended due to: {reason}. Polling stopped.");
    }

    public void Resume(SuspendReason reason)
    {
        bool canResume = false;
        lock (_lock)
        {
            _activeSuspendReasons.Remove(reason);
            canResume = _activeSuspendReasons.Count == 0;
        }

        if (canResume)
        {
            _uiBridge.Post(() =>
            {
                RestoreWidgetVisibilities();
                ResumeAllPolling();
            });

            State = ModuleState.Running;
            AppLogger.Log($"[WidgetsModule] Resumed after clearing {reason}. Polling restored.");
        }
    }

    private void OnFullscreenChanged(FullscreenChanged evt)
    {
        var settings = SettingsManager.Current;
        if (evt.IsFullscreen && (settings.WallpaperPauseOnFullscreen || settings.PauseGraphicsOnFullscreen))
        {
            Suspend(SuspendReason.Fullscreen);
        }
        else if (!evt.IsFullscreen)
        {
            Resume(SuspendReason.Fullscreen);
        }
    }

    private void OnPowerStateChanged(PowerStateChanged evt)
    {
        var settings = SettingsManager.Current;
        if (evt.IsOnBattery && settings.LowPowerMode)
        {
            Suspend(SuspendReason.Battery);
        }
        else if (!evt.IsOnBattery)
        {
            Resume(SuspendReason.Battery);
        }
    }

    private void OnSettingsChanged(SettingsChanged evt)
    {
        if (evt.SectionName.Equals("widgets", StringComparison.OrdinalIgnoreCase) ||
            evt.SectionName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            _uiBridge.Post(ApplySettingsToWidgets);
        }
    }

    private void EnsureWidgetsCreated()
    {
        if (_clockWidget == null)
        {
            _clockWidget = new DesktopClockWidget();
        }

        if (_cpuWidget == null)
        {
            _cpuWidget = new DesktopCpuWidget();
            if (_cpuWidget.Left == 0 && _cpuWidget.Top == 0)
            {
                _cpuWidget.Left = 24;
                _cpuWidget.Top = 100;
            }
        }

        if (_gpuWidget == null)
        {
            _gpuWidget = new DesktopGpuWidget();
            if (_gpuWidget.Left == 0 && _gpuWidget.Top == 0)
            {
                _gpuWidget.Left = 24;
                _gpuWidget.Top = 190;
            }
        }

        if (_ramWidget == null)
        {
            _ramWidget = new DesktopRamWidget();
            if (_ramWidget.Left == 0 && _ramWidget.Top == 0)
            {
                _ramWidget.Left = 24;
                _ramWidget.Top = 280;
            }
        }
    }

    public void ApplySettingsToWidgets()
    {
        var settings = SettingsManager.Current;
        EnsureWidgetsCreated();

        // Apply Opacity
        double opacity = Math.Clamp(settings.WidgetsOpacity / 100.0, 0.1, 1.0);
        if (_clockWidget != null) _clockWidget.Opacity = opacity;
        if (_cpuWidget != null) _cpuWidget.Opacity = opacity;
        if (_gpuWidget != null) _gpuWidget.Opacity = opacity;
        if (_ramWidget != null) _ramWidget.Opacity = opacity;

        // Apply Visibilities
        if (!IsPaused)
        {
            RestoreWidgetVisibilities();
        }
    }

    private void RestoreWidgetVisibilities()
    {
        var settings = SettingsManager.Current;

        SetWidgetVisibility(_clockWidget, settings.ShowClockWidget);
        SetWidgetVisibility(_cpuWidget, settings.ShowCpuWidget);
        SetWidgetVisibility(_gpuWidget, settings.ShowGpuWidget);
        SetWidgetVisibility(_ramWidget, settings.ShowRamWidget);
    }

    private static void SetWidgetVisibility(DesktopWidgetBase? widget, bool visible)
    {
        if (widget == null) return;
        if (visible)
        {
            widget.Show();
            widget.ResumePolling();
            widget.PinToDesktopBottom();
        }
        else
        {
            widget.PausePolling();
            widget.Hide();
        }
    }

    private void HideAllWidgets()
    {
        try { _clockWidget?.Hide(); } catch { }
        try { _cpuWidget?.Hide(); } catch { }
        try { _gpuWidget?.Hide(); } catch { }
        try { _ramWidget?.Hide(); } catch { }
    }

    public void PauseAllPolling()
    {
        try { _clockWidget?.PausePolling(); } catch { }
        try { _cpuWidget?.PausePolling(); } catch { }
        try { _gpuWidget?.PausePolling(); } catch { }
        try { _ramWidget?.PausePolling(); } catch { }
    }

    public void ResumeAllPolling()
    {
        try { _clockWidget?.ResumePolling(); } catch { }
        try { _cpuWidget?.ResumePolling(); } catch { }
        try { _gpuWidget?.ResumePolling(); } catch { }
        try { _ramWidget?.ResumePolling(); } catch { }
    }

    public void ResetAllPositions()
    {
        _uiBridge.Post(() =>
        {
            var wa = System.Windows.SystemParameters.WorkArea;
            if (_clockWidget != null)
            {
                _clockWidget.Left = wa.Right - _clockWidget.ActualWidth - 16;
                _clockWidget.Top = wa.Top + 60;
            }
            if (_cpuWidget != null)
            {
                _cpuWidget.Left = wa.Right - _cpuWidget.ActualWidth - 16;
                _cpuWidget.Top = wa.Top + 240;
            }
            if (_gpuWidget != null)
            {
                _gpuWidget.Left = wa.Right - _gpuWidget.ActualWidth - 16;
                _gpuWidget.Top = wa.Top + 330;
            }
            if (_ramWidget != null)
            {
                _ramWidget.Left = wa.Right - _ramWidget.ActualWidth - 16;
                _ramWidget.Top = wa.Top + 420;
            }
        });
    }

    private void CloseAllWidgets()
    {
        try { _clockWidget?.Close(); _clockWidget = null; } catch { }
        try { _cpuWidget?.Close(); _cpuWidget = null; } catch { }
        try { _gpuWidget?.Close(); _gpuWidget = null; } catch { }
        try { _ramWidget?.Close(); _ramWidget = null; } catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _ = StopAsync();
    }
}
