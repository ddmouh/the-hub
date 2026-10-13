using TheHub.Common.Config;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using TheHub.Core.Radial;

namespace TheHub.Overlays.RadialLauncher;

/// <summary>
/// Radial Launcher module implementing IHubModule lifecycle.
/// Manages RadialWindow and integrates with RadialGestureRecognizer off the UI thread.
/// Respects FullscreenProtection via PowerPolicy/HubBus, automatically suppressing the wheel in fullscreen games.
/// </summary>
public class RadialModule : IHubModule
{
    public string Id => "RadialLauncher";
    public ModuleState State { get; private set; } = ModuleState.Stopped;

    private readonly IHubBus _bus;
    private readonly UiBridge _uiBridge;
    private readonly RadialGestureRecognizer _recognizer;
    private RadialWindow? _window;
    private readonly List<IDisposable> _subscriptions = new();
    private bool _isDisposed;
    private bool _isSuspendedByFullscreen;

    public RadialGestureRecognizer Recognizer => _recognizer;

    public RadialModule(IHubBus? bus = null, UiBridge? uiBridge = null)
    {
        _bus = bus ?? HubBus.Default;
        _uiBridge = uiBridge ?? UiBridge.Instance;
        _recognizer = new RadialGestureRecognizer();

        ConfigureRecognizerFromSettings();
        WireRecognizerEvents();
    }

    private void ConfigureRecognizerFromSettings()
    {
        var settings = SettingsManager.Current;
        _recognizer.HoldDelayMs = settings.HoldDelayMs;
        _recognizer.ActivationZone = settings.ActivationZone;
        _recognizer.CenterRadius = settings.RadialCenterRadius;
        _recognizer.GestureBehavior = settings.GestureBehavior ?? "Hold";
        _recognizer.TriggerButton = settings.MouseTriggerButton switch
        {
            "Right" => RadialGestureRecognizer.MouseButtonRight,
            "Left" => RadialGestureRecognizer.MouseButtonLeft,
            "X1" => RadialGestureRecognizer.MouseButtonX1,
            "X2" => RadialGestureRecognizer.MouseButtonX2,
            _ => RadialGestureRecognizer.MouseButtonMiddle
        };
    }

    private void WireRecognizerEvents()
    {
        _recognizer.Opened += (x, y) =>
        {
            if (_isSuspendedByFullscreen) return;

            _uiBridge.Post(() =>
            {
                EnsureWindowCreated();
                _window?.ShowAt(x, y);
            });
        };

        _recognizer.PointerMoved += (x, y, angle, dist, slice) =>
        {
            if (_isSuspendedByFullscreen) return;

            _uiBridge.Post(() =>
            {
                _window?.UpdatePointer(x, y);
            });
        };

        _recognizer.Confirmed += (slice) =>
        {
            if (_isSuspendedByFullscreen) return;

            _uiBridge.Post(() =>
            {
                _window?.TriggerSelectionAndClose();
            });
        };

        _recognizer.Cancelled += () =>
        {
            _uiBridge.Post(() =>
            {
                _window?.Dismiss();
            });
        };
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (State == ModuleState.Running) return;

        await _uiBridge.InvokeAsync(() =>
        {
            EnsureWindowCreated();
        });

        SubscribeToEvents();
        State = ModuleState.Running;
    }

    public async Task StopAsync()
    {
        if (State == ModuleState.Stopped) return;

        UnsubscribeFromEvents();

        await _uiBridge.InvokeAsync(() =>
        {
            try { _window?.Dismiss(); } catch { }
        });

        State = ModuleState.Stopped;
    }

    public void Suspend(SuspendReason reason)
    {
        if (reason == SuspendReason.Fullscreen)
        {
            _isSuspendedByFullscreen = true;
        }

        _uiBridge.Post(() =>
        {
            try { _window?.Dismiss(); } catch { }
        });

        State = ModuleState.Suspended;
    }

    public void Resume(SuspendReason reason)
    {
        if (reason == SuspendReason.Fullscreen)
        {
            _isSuspendedByFullscreen = false;
        }

        State = ModuleState.Running;
    }

    private void EnsureWindowCreated()
    {
        if (_window == null)
        {
            _window = new RadialWindow();
        }
    }

    public void ShowAt(int x, int y)
    {
        if (_isSuspendedByFullscreen) return;

        _uiBridge.Post(() =>
        {
            EnsureWindowCreated();
            _window?.ShowAt(x, y);
        });
    }

    public void Dismiss()
    {
        _uiBridge.Post(() =>
        {
            _window?.Dismiss();
        });
    }

    public void RefreshAppearance()
    {
        ConfigureRecognizerFromSettings();
        _uiBridge.Post(() =>
        {
            if (_window != null)
            {
                _window.UpdateAppearance(SettingsManager.Current);
                _window.RefreshShortcuts();
            }
        });
    }

    private void SubscribeToEvents()
    {
        UnsubscribeFromEvents();

        _subscriptions.Add(_bus.Subscribe<FullscreenChanged>(evt =>
        {
            if (!SettingsManager.Current.FullscreenProtection) return;

            if (evt.IsFullscreen)
            {
                Suspend(SuspendReason.Fullscreen);
            }
            else
            {
                Resume(SuspendReason.Fullscreen);
            }
        }));

        _subscriptions.Add(_bus.Subscribe<SettingsChanged>(evt =>
        {
            if (evt.SectionName.Equals("radial", StringComparison.OrdinalIgnoreCase))
            {
                RefreshAppearance();
            }
        }));
    }

    private void UnsubscribeFromEvents()
    {
        foreach (var sub in _subscriptions)
        {
            try { sub.Dispose(); } catch { }
        }
        _subscriptions.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        UnsubscribeFromEvents();

        _uiBridge.Post(() =>
        {
            try
            {
                _window?.Close();
                _window = null;
            }
            catch { }
        });
    }
}
