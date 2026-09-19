using System.Windows;
using TheHub.Common.IPC;
using TheHub.Common.Models;
using TheHub.Overlays.DynamicIsland;
using TheHub.Overlays.RadialLauncher;

namespace TheHub.Overlays;

public partial class App : Application
{
    private RadialWindow? _radialWindow;
    private DynamicIslandWindow? _islandWindow;
    private NamedPipeServer? _pipeServer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _radialWindow = new RadialWindow();
        _islandWindow = new DynamicIslandWindow();
        _islandWindow.Show();

        // Listen on Overlay pipe
        _pipeServer = new NamedPipeServer(IpcConstants.OverlayPipeName, HandleOverlayIpcAsync);
        _pipeServer.Start();
    }

    private Task<IpcMessage?> HandleOverlayIpcAsync(IpcMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            switch (msg.Type)
            {
                case IpcMessageType.ShowRadialLauncher:
                    var trigger = msg.GetData<RadialTriggerPayload>();
                    if (trigger != null)
                    {
                        _radialWindow?.ShowAt(trigger.CursorX, trigger.CursorY);
                    }
                    break;

                case IpcMessageType.HideRadialLauncher:
                    _radialWindow?.Hide();
                    break;

                case IpcMessageType.ShowDynamicIslandEvent:
                    var evt = msg.GetData<DynamicIslandEvent>();
                    if (evt != null)
                    {
                        _islandWindow?.PostEvent(evt);
                    }
                    break;

                case IpcMessageType.SettingsUpdated:
                    _radialWindow?.RefreshShortcuts();
                    _islandWindow?.PositionWindow();
                    break;
            }
        });

        return Task.FromResult<IpcMessage?>(null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeServer?.DisposeAsync().AsTask().Wait(500);
        base.OnExit(e);
    }
}
