using System.Text.Json;

namespace TheHub.Common.IPC;

public static class IpcConstants
{
    public const string CorePipeName = "TheHub_Core_IPC";
    public const string OverlayPipeName = "TheHub_Overlay_IPC";
}

public enum IpcMessageType
{
    Ping,
    Pong,
    ShowRadialLauncher,
    HideRadialLauncher,
    ShowDynamicIslandEvent,
    HideDynamicIsland,
    LaunchShortcut,
    ApplyWorkspace,
    SettingsUpdated,
    EmergencyKillSwitchTriggered
}

public class IpcMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public IpcMessageType Type { get; set; }
    public string PayloadJson { get; set; } = string.Empty;

    public static IpcMessage Create<T>(IpcMessageType type, T data)
    {
        return new IpcMessage
        {
            Type = type,
            PayloadJson = JsonSerializer.Serialize(data)
        };
    }

    public T? GetData<T>()
    {
        if (string.IsNullOrWhiteSpace(PayloadJson))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(PayloadJson);
        }
        catch
        {
            return default;
        }
    }
}

public class RadialTriggerPayload
{
    public int CursorX { get; set; }
    public int CursorY { get; set; }
    public bool TriggeredByKeyboard { get; set; }
}
