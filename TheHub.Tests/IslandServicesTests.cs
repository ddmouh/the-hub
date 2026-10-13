using TheHub.Core.Bus;
using TheHub.Core.Services;
using Xunit;

namespace TheHub.Tests;

public class IslandServicesTests
{
    [Fact]
    public void PrayerTimeService_CalculatesValidChronologicalSchedule()
    {
        var bus = new HubBus();
        var service = new PrayerTimeService(bus)
        {
            Latitude = 24.7136, // Riyadh
            Longitude = 46.6753
        };

        var date = new DateTime(2026, 6, 15);
        var schedule = service.CalculatePrayers(date);

        Assert.True(schedule.Fajr < schedule.Sunrise, $"Fajr ({schedule.Fajr}) must be before Sunrise ({schedule.Sunrise})");
        Assert.True(schedule.Sunrise < schedule.Dhuhr, $"Sunrise ({schedule.Sunrise}) must be before Dhuhr ({schedule.Dhuhr})");
        Assert.True(schedule.Dhuhr < schedule.Asr, $"Dhuhr ({schedule.Dhuhr}) must be before Asr ({schedule.Asr})");
        Assert.True(schedule.Asr < schedule.Maghrib, $"Asr ({schedule.Asr}) must be before Maghrib ({schedule.Maghrib})");
        Assert.True(schedule.Maghrib < schedule.Isha, $"Maghrib ({schedule.Maghrib}) must be before Isha ({schedule.Isha})");
    }

    [Fact]
    public async Task PomodoroService_StartsAndPublishesTick()
    {
        var bus = new HubBus();
        var service = new PomodoroService(bus)
        {
            FocusMinutes = 25,
            ShortBreakMinutes = 5
        };

        var tcs = new TaskCompletionSource<PomodoroTick>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = bus.Subscribe<PomodoroTick>(tick => tcs.TrySetResult(tick));

        service.Start();

        var received = await Task.WhenAny(tcs.Task, Task.Delay(2000)) == tcs.Task ? await tcs.Task : null;

        Assert.True(service.IsRunning);
        Assert.NotNull(received);
        Assert.Equal("Focus", received.Phase);
        Assert.Equal(25 * 60, received.RemainingSeconds);

        service.Pause();
        Assert.False(service.IsRunning);

        service.SkipToNextPhase();
        Assert.Equal(PomodoroPhase.ShortBreak, service.CurrentPhase);
        Assert.Equal(5 * 60, service.RemainingSeconds);
    }

    [Fact]
    public async Task UsbWatcher_HandleDeviceChange_PublishesEvent()
    {
        var bus = new HubBus();
        var watcher = new UsbWatcher(bus, enableDrivePolling: false);

        var tcs = new TaskCompletionSource<UsbChanged>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = bus.Subscribe<UsbChanged>(e => tcs.TrySetResult(e));

        watcher.HandleDeviceChange(true, "SanDisk Ultra 3.0");

        var received = await Task.WhenAny(tcs.Task, Task.Delay(2000)) == tcs.Task ? await tcs.Task : null;

        Assert.NotNull(received);
        Assert.Equal("SanDisk Ultra 3.0", received.DeviceName);
        Assert.True(received.IsConnected);
    }

    [Fact]
    public async Task AudioService_QueueProcessesSerialVolumeRequests()
    {
        var service = new AudioService();
        // Fire 5 rapid volume requests, none should throw or hang
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => service.GetMasterVolumeStateAsync())
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(5, results.Length);
    }
}
