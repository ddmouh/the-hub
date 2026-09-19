using System;
using System.Diagnostics;
using System.Threading.Tasks;
using TheHub.Core.Native;

namespace TheHub.Core.Services;

public interface IBrightnessService
{
    Task<int> GetBrightnessAsync();
    Task SetBrightnessAsync(int brightnessPercent, int monitorIndex = 0);
}

public class BrightnessService : IBrightnessService
{
    public Task<int> GetBrightnessAsync()
    {
        return Task.Run(() => SystemControlHelper.GetScreenBrightness());
    }

    public Task SetBrightnessAsync(int brightnessPercent, int monitorIndex = 0)
    {
        return Task.Run(() =>
        {
            try
            {
                int clamped = Math.Clamp(brightnessPercent, 10, 100);
                SystemControlHelper.SetExactScreenBrightness(clamped);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrightnessService] SetBrightnessAsync error: {ex.Message}");
            }
        });
    }
}
