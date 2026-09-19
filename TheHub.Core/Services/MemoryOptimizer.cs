using System;
using System.Diagnostics;
using TheHub.Common.Config;
using TheHub.Core.Native;

namespace TheHub.Core.Services;

/// <summary>
/// Provides working set trimming and garbage collection optimizations to keep RAM usage minimal.
/// </summary>
public static class MemoryOptimizer
{
    private static readonly object _trimLock = new();
    private static DateTime _lastTrim = DateTime.MinValue;

    /// <summary>
    /// Forces a compact Gen 2 garbage collection and flushes the process working set via EmptyWorkingSet.
    /// Safely throttled to avoid excessive calls within short bursts.
    /// </summary>
    public static void TrimMemory(bool force = false)
    {
        lock (_trimLock)
        {
            if (!force && (DateTime.UtcNow - _lastTrim).TotalSeconds < 2)
            {
                return;
            }
            _lastTrim = DateTime.UtcNow;

            try
            {
                long beforeBytes = Process.GetCurrentProcess().WorkingSet64;

                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true);

                using var process = Process.GetCurrentProcess();
                Win32Api.EmptyWorkingSet(process.Handle);

                long afterBytes = Process.GetCurrentProcess().WorkingSet64;
                AppLogger.Log($"[MemoryOptimizer] Working set trimmed: {beforeBytes / 1024 / 1024:N1} MB -> {afterBytes / 1024 / 1024:N1} MB");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[MemoryOptimizer] TrimMemory error: {ex.Message}");
            }
        }
    }
}
