using System;
using System.IO;
using System.Threading.Tasks;
using TheHub.Core.AI;
using TheHub.Core.Bus;
using TheHub.Core.Modules;
using Xunit;

namespace TheHub.Tests;

public class GateP7AssistantTests
{
    [Fact]
    public async Task AIIntentParser_ClassifiesTiersAndArgumentsCorrectly()
    {
        var parser = new AIIntentParser();

        // Level 0: Safe
        var metricIntent = await parser.ParseAsync("check my cpu and ram stats");
        Assert.Equal("read_metrics", metricIntent.ToolName);
        Assert.Equal(AIPermissionLevel.Level0_Safe, metricIntent.Level);

        // Level 1: Low Risk
        var volIntent = await parser.ParseAsync("set volume to 65%");
        Assert.Equal("set_volume", volIntent.ToolName);
        Assert.Equal(AIPermissionLevel.Level1_LowRisk, volIntent.Level);
        Assert.Equal("65", volIntent.Arguments["volume"]);

        var wsIntent = await parser.ParseAsync("switch to workspace Gaming");
        Assert.Equal("switch_workspace", wsIntent.ToolName);
        Assert.Equal(AIPermissionLevel.Level1_LowRisk, wsIntent.Level);
        Assert.Equal("Gaming", wsIntent.Arguments["workspace"]);

        // Level 2: Elevated
        var launchIntent = await parser.ParseAsync("launch notepad.exe");
        Assert.Equal("launch_app", launchIntent.ToolName);
        Assert.Equal(AIPermissionLevel.Level2_Elevated, launchIntent.Level);
        Assert.Equal("notepad.exe", launchIntent.Arguments["app"]);

        // Level 3: Dangerous
        var scriptIntent = await parser.ParseAsync("run powershell -c rm dummy.txt");
        Assert.Equal("execute_system_script", scriptIntent.ToolName);
        Assert.Equal(AIPermissionLevel.Level3_Dangerous, scriptIntent.Level);
    }

    [Fact]
    public async Task AIAssistantModule_PermissionSandbox_BlocksLevel3_RequiresConfirmationForLevel2()
    {
        var tempLogPath = Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid():N}.jsonl");
        try
        {
            var audit = new AIAuditLogger(tempLogPath);
            var module = new AIAssistantModule(auditLogger: audit);
            await module.StartAsync();

            // 1. Level 0: Auto executes
            var r0 = await module.ProcessPromptAsync("what is my cpu usage");
            Assert.True(r0.Success);
            Assert.Contains("CPU:", r0.Message);

            // 2. Level 1: Auto executes
            var r1 = await module.ProcessPromptAsync("set volume to 40");
            Assert.True(r1.Success);
            Assert.Contains("Volume set to 40%", r1.Message);

            // 3. Level 2 without confirmation callback -> denied
            module.ConfirmElevatedActionAsync = _ => Task.FromResult(false);
            var r2Denied = await module.ProcessPromptAsync("open steam");
            Assert.False(r2Denied.Success);
            Assert.Contains("requires user confirmation and was not approved", r2Denied.Message);

            // 4. Level 2 with confirmation callback -> allowed
            module.ConfirmElevatedActionAsync = _ => Task.FromResult(true);
            var r2Approved = await module.ProcessPromptAsync("open steam");
            Assert.True(r2Approved.Success);
            Assert.Contains("Application 'steam' launched", r2Approved.Message);

            // 5. Level 3 Dangerous -> blocked unconditionally
            var r3 = await module.ProcessPromptAsync("powershell -c format d:");
            Assert.False(r3.Success);
            Assert.Contains("Blocked by security sandbox", r3.Message);

            // Verify Audit Trail has entries for each
            var entries = audit.ReadRecentEntries(10);
            Assert.True(entries.Count >= 5);
        }
        finally
        {
            if (File.Exists(tempLogPath)) File.Delete(tempLogPath);
        }
    }

    [Fact]
    public async Task AIAssistantModule_EmergencyKillSwitch_HaltsExecutionUnderOneSecond()
    {
        var tempLogPath = Path.Combine(Path.GetTempPath(), $"audit_kill_{Guid.NewGuid():N}.jsonl");
        try
        {
            var audit = new AIAuditLogger(tempLogPath);
            var bus = new HubBus();
            var module = new AIAssistantModule(bus: bus, auditLogger: audit);
            await module.StartAsync();

            Assert.False(module.IsKillSwitchActive);
            Assert.Equal(ModuleState.Running, module.State);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            module.TriggerEmergencyKillSwitch();
            sw.Stop();

            // Emergency stop takes far less than 1 second (< 100ms)
            Assert.True(sw.ElapsedMilliseconds < 1000);
            Assert.True(module.IsKillSwitchActive);
            Assert.Equal(ModuleState.Suspended, module.State);

            // Attempting prompt while killed is immediately rejected
            var result = await module.ProcessPromptAsync("what is my cpu usage");
            Assert.False(result.Success);
            Assert.Contains("Emergency Kill Switch is currently active", result.Message);

            // Resetting kill switch restores operational state
            module.ResetKillSwitch();
            Assert.False(module.IsKillSwitchActive);
            Assert.Equal(ModuleState.Running, module.State);

            var restoredResult = await module.ProcessPromptAsync("what is my cpu usage");
            Assert.True(restoredResult.Success);
        }
        finally
        {
            if (File.Exists(tempLogPath)) File.Delete(tempLogPath);
        }
    }

    [Fact]
    public void AIAuditLogger_WritesAndReadsJsonlCorrectly()
    {
        var tempLogPath = Path.Combine(Path.GetTempPath(), $"audit_rw_{Guid.NewGuid():N}.jsonl");
        try
        {
            var logger = new AIAuditLogger(tempLogPath);
            logger.Log(new AIAuditEntry(
                DateTime.UtcNow,
                "test_action",
                AIPermissionLevel.Level1_LowRisk,
                "EXECUTED",
                new() { { "key", "value" } },
                "test note"
            ));

            var entries = logger.ReadRecentEntries();
            Assert.Single(entries);
            Assert.Equal("test_action", entries[0].Intent);
            Assert.Equal(AIPermissionLevel.Level1_LowRisk, entries[0].Level);
            Assert.Equal("EXECUTED", entries[0].Status);
            Assert.Equal("value", entries[0].Parameters?["key"]);
        }
        finally
        {
            if (File.Exists(tempLogPath)) File.Delete(tempLogPath);
        }
    }
}
