using System.Windows.Threading;
using TheHub.Core.Bridge;
using Xunit;

namespace TheHub.Tests;

public class UiBridgeTests
{
    private class FakeDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queue = new();
        private readonly object _lock = new();

        public int InvocationCount { get; private set; }

        public void InvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            lock (_lock)
            {
                InvocationCount++;
                _queue.Enqueue(action);
            }
        }

        public void Drain()
        {
            List<Action> actions;
            lock (_lock)
            {
                actions = new List<Action>(_queue);
                _queue.Clear();
            }

            foreach (var act in actions)
            {
                act();
            }
        }
    }

    [Fact]
    public void UiBridge_Post_DispatchesAction()
    {
        var fake = new FakeDispatcher();
        var bridge = new UiBridge(fake);
        bool executed = false;

        bridge.Post(() => executed = true);

        Assert.Equal(1, fake.InvocationCount);
        Assert.False(executed); // Not drained yet

        fake.Drain();
        Assert.True(executed);
    }

    [Fact]
    public void UiBridge_PostCoalesced_ExecutesOnlyLatestValue()
    {
        var fake = new FakeDispatcher();
        var bridge = new UiBridge(fake);
        int finalValue = -1;
        int executionCount = 0;

        // Simulate 20 high-frequency volume changes
        for (int i = 1; i <= 20; i++)
        {
            int val = i;
            bridge.PostCoalesced("volume_slider", () =>
            {
                executionCount++;
                finalValue = val;
            });
        }

        // Only 1 pending dispatch should have been scheduled
        Assert.Equal(1, fake.InvocationCount);

        // Execute the scheduled callback
        fake.Drain();

        // Exactly one action ran, with the latest value (20)
        Assert.Equal(1, executionCount);
        Assert.Equal(20, finalValue);
    }

    [Fact]
    public void UiBridge_PostBatch_ExecutesAllInOrder()
    {
        var fake = new FakeDispatcher();
        var bridge = new UiBridge(fake);
        var order = new List<int>();

        var actions = Enumerable.Range(1, 5).Select(i => (Action)(() => order.Add(i)));
        bridge.PostBatch(actions);

        Assert.Equal(1, fake.InvocationCount);
        fake.Drain();

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, order);
    }
}
