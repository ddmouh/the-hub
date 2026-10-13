using System.Windows.Threading;
using TheHub.Core.Bridge;
using TheHub.Core.Diagnostics;
using Xunit;

namespace TheHub.Tests;

public class DiagnosticsTests
{
    private class BlockingDispatcher : IUiDispatcher
    {
        public bool SimulateFreeze { get; set; } = true;
        public readonly List<Action> QueuedActions = new();

        public void InvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            if (SimulateFreeze)
            {
                // UI thread is blocked / frozen, actions accumulate and don't execute
                QueuedActions.Add(action);
            }
            else
            {
                action();
            }
        }
    }

    [Fact]
    public void Breadcrumbs_AddsAndRetrievesRecent()
    {
        Breadcrumbs.Clear();

        for (int i = 1; i <= 25; i++)
        {
            Breadcrumbs.Add($"step:{i}");
        }

        var recent = Breadcrumbs.GetRecent(20);
        Assert.Equal(20, recent.Count);
        Assert.Equal("step:6", recent[0].Message);
        Assert.Equal("step:25", recent[^1].Message);
    }

    [Fact]
    public void UiWatchdog_DetectsUnresponsiveUI_TriggersLogAndEvent()
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add("island:expand");
        Breadcrumbs.Add("volume:change:80");

        var blockedDispatcher = new BlockingDispatcher { SimulateFreeze = true };
        var logs = new List<string>();
        using var signal = new ManualResetEventSlim(false);

        using var watchdog = new UiWatchdog(
            blockedDispatcher,
            timeout: TimeSpan.FromMilliseconds(50),
            logger: msg =>
            {
                logs.Add(msg);
                signal.Set();
            });

        // Wait for the watchdog to detect the unresponsive dispatcher
        bool detected = signal.Wait(TimeSpan.FromSeconds(3));

        Assert.True(detected, "Watchdog must detect blocked UI dispatcher and log stall");
        Assert.Single(logs);
        Assert.Contains("UI thread stall detected!", logs[0]);
        Assert.Contains("island:expand", logs[0]);
        Assert.Contains("volume:change:80", logs[0]);
    }
}
