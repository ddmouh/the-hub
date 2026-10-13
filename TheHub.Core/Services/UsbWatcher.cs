using System.IO;
using TheHub.Common.Config;
using TheHub.Core.Bus;

namespace TheHub.Core.Services;

/// <summary>
/// Monitors USB and removable device connection and disconnection events.
/// Publishes UsbChanged to IHubBus.
/// </summary>
public class UsbWatcher : IDisposable
{
    private readonly IHubBus _bus;
    private readonly System.Threading.Timer? _pollTimer;
    private HashSet<string> _knownDrives = new(StringComparer.OrdinalIgnoreCase);
    private bool _isInitialized;

    public UsbWatcher(IHubBus? bus = null, bool enableDrivePolling = true)
    {
        _bus = bus ?? HubBus.Default;

        if (enableDrivePolling)
        {
            _knownDrives = GetRemovableDrives();
            _isInitialized = true;
            _pollTimer = new System.Threading.Timer(OnPollDrives, null, 2000, 2000);
        }
    }

    public void HandleDeviceChange(bool isArrival, string? deviceName = null)
    {
        string name = string.IsNullOrWhiteSpace(deviceName) ? (isArrival ? "USB Device Connected" : "USB Device Removed") : deviceName;
        _bus.Publish(new UsbChanged(name, isArrival));
    }

    private void OnPollDrives(object? state)
    {
        try
        {
            var currentDrives = GetRemovableDrives();
            if (!_isInitialized)
            {
                _knownDrives = currentDrives;
                _isInitialized = true;
                return;
            }

            // Detect new arrivals
            foreach (var d in currentDrives)
            {
                if (!_knownDrives.Contains(d))
                {
                    string label = GetDriveLabel(d);
                    _bus.Publish(new UsbChanged(label, true));
                }
            }

            // Detect removals
            foreach (var d in _knownDrives)
            {
                if (!currentDrives.Contains(d))
                {
                    _bus.Publish(new UsbChanged($"Drive ({d})", false));
                }
            }

            _knownDrives = currentDrives;
        }
        catch
        {
        }
    }

    private static HashSet<string> GetRemovableDrives()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Removable || drive.DriveType == DriveType.CDRom)
                {
                    set.Add(drive.Name);
                }
            }
        }
        catch { }
        return set;
    }

    private static string GetDriveLabel(string driveName)
    {
        try
        {
            var info = new DriveInfo(driveName);
            string vol = info.VolumeLabel;
            return !string.IsNullOrWhiteSpace(vol) ? $"{vol} ({driveName.TrimEnd('\\')})" : $"Removable Disk ({driveName.TrimEnd('\\')})";
        }
        catch
        {
            return $"USB Drive ({driveName.TrimEnd('\\')})";
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
    }
}
