using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TheHub.Core.Bridge;
using TheHub.Core.Bus;
using TheHub.Core.Modules;

namespace TheHub.Core.AI;

public class AIAssistantModule : IHubModule
{
    private readonly HubBus _bus;
    private readonly UiBridge _uiBridge;
    private readonly AIAuditLogger _auditLogger;
    private readonly AIIntentParser _intentParser;
    private readonly ConcurrentDictionary<string, IAITool> _tools = new();

    private ModuleState _state = ModuleState.Stopped;
    private bool _isKillSwitchActive;
    private CancellationTokenSource? _killSwitchCts;
    private readonly object _lock = new();

    public string Id => "ai-assistant";
    public ModuleState State => _state;
    public bool IsKillSwitchActive => _isKillSwitchActive;
    public AIAuditLogger AuditLogger => _auditLogger;

    /// <summary>
    /// Callback invoked when a Level 2 (Elevated) action requires user confirmation.
    /// Defaults to auto-confirm if null (e.g. headless tests), or a UI modal prompt in production.
    /// </summary>
    public Func<AIToolIntent, Task<bool>>? ConfirmElevatedActionAsync { get; set; }

    public AIAssistantModule(
        HubBus? bus = null,
        UiBridge? uiBridge = null,
        AIAuditLogger? auditLogger = null,
        AIIntentParser? intentParser = null)
    {
        _bus = bus ?? HubBus.Default;
        _uiBridge = uiBridge ?? UiBridge.Instance;
        _auditLogger = auditLogger ?? new AIAuditLogger();
        _intentParser = intentParser ?? new AIIntentParser();

        RegisterDefaultTools();
    }

    private void RegisterDefaultTools()
    {
        RegisterTool(new ReadMetricsTool());
        RegisterTool(new SetVolumeTool());
        RegisterTool(new SwitchWorkspaceTool());
        RegisterTool(new SetWallpaperTool());
        RegisterTool(new LaunchAppTool());
        RegisterTool(new CloseAppTool());
        RegisterTool(new ExecuteSystemScriptTool());
    }

    public void RegisterTool(IAITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _killSwitchCts = new CancellationTokenSource();
            _isKillSwitchActive = false;
            _state = ModuleState.Running;
        }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_lock)
        {
            _killSwitchCts?.Cancel();
            _state = ModuleState.Stopped;
        }
        return Task.CompletedTask;
    }

    public void Suspend(SuspendReason reason)
    {
        lock (_lock)
        {
            if (_state == ModuleState.Running)
            {
                _state = ModuleState.Suspended;
            }
        }
    }

    public void Resume(SuspendReason reason)
    {
        lock (_lock)
        {
            if (_state == ModuleState.Suspended && !_isKillSwitchActive)
            {
                _state = ModuleState.Running;
            }
        }
    }

    public void TriggerEmergencyKillSwitch()
    {
        lock (_lock)
        {
            _isKillSwitchActive = true;
            _killSwitchCts?.Cancel();
            _killSwitchCts = new CancellationTokenSource();
            _state = ModuleState.Suspended;

            _auditLogger.Log(new AIAuditEntry(
                DateTime.UtcNow,
                "EMERGENCY_KILL_SWITCH",
                AIPermissionLevel.Level3_Dangerous,
                "KILL_SWITCH_ACTIVATED",
                null,
                "User or safety policy activated emergency kill switch"
            ));
        }

        _bus.Publish(new AIKillSwitchTriggered(
            true,
            "Emergency AI Kill Switch triggered. All AI execution halted."
        ));
    }

    public void ResetKillSwitch()
    {
        lock (_lock)
        {
            _isKillSwitchActive = false;
            _state = ModuleState.Running;
            _auditLogger.Log(new AIAuditEntry(
                DateTime.UtcNow,
                "RESET_KILL_SWITCH",
                AIPermissionLevel.Level0_Safe,
                "RESET",
                null,
                "Kill switch reset by user"
            ));
        }

        _bus.Publish(new AIKillSwitchTriggered(
            false,
            "AI Kill Switch reset by user."
        ));
    }

    public async Task<AIToolExecutionResult> ProcessPromptAsync(string prompt, CancellationToken ct = default)
    {
        if (_isKillSwitchActive)
        {
            return new AIToolExecutionResult(false, "Action aborted: Emergency Kill Switch is currently active.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _killSwitchCts?.Token ?? CancellationToken.None);
        var token = linkedCts.Token;

        var intent = await _intentParser.ParseAsync(prompt, token);

        if (intent.ToolName == "unknown" || !_tools.TryGetValue(intent.ToolName, out var tool))
        {
            _auditLogger.Log(new AIAuditEntry(
                DateTime.UtcNow,
                prompt,
                AIPermissionLevel.Level0_Safe,
                "UNKNOWN_INTENT",
                intent.Arguments
            ));
            return new AIToolExecutionResult(false, $"I did not understand the request: '{prompt}'");
        }

        // Tier 3: Blocked by default
        if (tool.Level == AIPermissionLevel.Level3_Dangerous)
        {
            _auditLogger.Log(new AIAuditEntry(
                DateTime.UtcNow,
                intent.ToolName,
                tool.Level,
                "BLOCKED_SECURITY_POLICY",
                intent.Arguments,
                "Action blocked: Level 3 Dangerous system scripts are prohibited."
            ));
            return new AIToolExecutionResult(false, "Blocked by security sandbox: Level 3 Dangerous actions are forbidden.");
        }

        // Tier 2: Elevated -> Requires confirmation
        if (tool.Level == AIPermissionLevel.Level2_Elevated)
        {
            bool confirmed = false;
            if (ConfirmElevatedActionAsync != null)
            {
                confirmed = await ConfirmElevatedActionAsync(intent);
            }

            if (!confirmed)
            {
                _auditLogger.Log(new AIAuditEntry(
                    DateTime.UtcNow,
                    intent.ToolName,
                    tool.Level,
                    "CONFIRMATION_DENIED",
                    intent.Arguments,
                    "User declined or did not confirm elevated execution."
                ));
                return new AIToolExecutionResult(false, $"Action '{intent.ToolName}' requires user confirmation and was not approved.");
            }
        }

        // Execute Tool (Level 0, Level 1, or Confirmed Level 2)
        try
        {
            var result = await tool.ExecuteAsync(intent.Arguments, token);
            _auditLogger.Log(new AIAuditEntry(
                DateTime.UtcNow,
                intent.ToolName,
                tool.Level,
                result.Success ? "EXECUTED" : "FAILED",
                intent.Arguments,
                result.Message
            ));
            return result;
        }
        catch (OperationCanceledException)
        {
            _auditLogger.Log(new AIAuditEntry(
                DateTime.UtcNow,
                intent.ToolName,
                tool.Level,
                "CANCELLED",
                intent.Arguments,
                "Action cancelled by kill switch or token"
            ));
            return new AIToolExecutionResult(false, "Action cancelled.");
        }
        catch (Exception ex)
        {
            _auditLogger.Log(new AIAuditEntry(
                DateTime.UtcNow,
                intent.ToolName,
                tool.Level,
                "ERROR",
                intent.Arguments,
                ex.Message
            ));
            return new AIToolExecutionResult(false, $"Error executing '{intent.ToolName}': {ex.Message}");
        }
    }

    public void Dispose()
    {
        _killSwitchCts?.Dispose();
    }
}
