using System.Collections.Concurrent;
using TheHub.Core.Bus;
using Xunit;

namespace TheHub.Tests;

public class HubBusTests
{
    [Fact]
    public async Task HubBus_PublishFrom8Threads_AllDelivered()
    {
        using var bus = new HubBus();
        const int threadCount = 8;
        const int eventsPerThread = 100;
        const int totalExpected = threadCount * eventsPerThread;

        int receivedCount = 0;
        using var signal = new ManualResetEventSlim(false);

        using var sub = bus.Subscribe<VolumeChanged>(evt =>
        {
            if (Interlocked.Increment(ref receivedCount) == totalExpected)
            {
                signal.Set();
            }
        });

        var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < eventsPerThread; i++)
            {
                bus.Publish(new VolumeChanged(i, false));
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        bool completed = signal.Wait(TimeSpan.FromSeconds(5));
        Assert.True(completed, $"Expected {totalExpected} events, but only received {receivedCount}");
        Assert.Equal(totalExpected, receivedCount);
    }

    [Fact]
    public void HubBus_OrderingPerPublisher_IsPreserved()
    {
        using var bus = new HubBus();
        const int count = 100;
        var received = new List<int>();
        using var signal = new ManualResetEventSlim(false);

        using var sub = bus.Subscribe<VolumeChanged>(evt =>
        {
            lock (received)
            {
                received.Add(evt.VolumePercent);
                if (received.Count == count) signal.Set();
            }
        });

        for (int i = 0; i < count; i++)
        {
            bus.Publish(new VolumeChanged(i, false));
        }

        bool completed = signal.Wait(TimeSpan.FromSeconds(3));
        Assert.True(completed);

        lock (received)
        {
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(i, received[i]);
            }
        }
    }

    [Fact]
    public void HubBus_SubscriberException_DoesNotBreakOtherSubscribers()
    {
        using var bus = new HubBus();
        bool receivedByHealthy = false;
        using var signal = new ManualResetEventSlim(false);

        // Faulty subscriber
        using var faultySub = bus.Subscribe<MicMuteChanged>(evt =>
        {
            throw new InvalidOperationException("Simulated subscriber crash");
        });

        // Healthy subscriber
        using var healthySub = bus.Subscribe<MicMuteChanged>(evt =>
        {
            receivedByHealthy = true;
            signal.Set();
        });

        bus.Publish(new MicMuteChanged(true));

        bool completed = signal.Wait(TimeSpan.FromSeconds(2));
        Assert.True(completed);
        Assert.True(receivedByHealthy, "Healthy subscriber must receive event even if earlier subscriber threw");
    }

    [Fact]
    public void HubBus_DisposeSubscription_Unsubscribes()
    {
        using var bus = new HubBus();
        int callCount = 0;

        var sub = bus.Subscribe<BrightnessChanged>(evt =>
        {
            Interlocked.Increment(ref callCount);
        });

        bus.Publish(new BrightnessChanged(50));
        Thread.Sleep(50);
        Assert.Equal(1, callCount);

        // Unsubscribe
        sub.Dispose();

        bus.Publish(new BrightnessChanged(75));
        Thread.Sleep(50);
        Assert.Equal(1, callCount);
    }
}
