using System;
using System.Collections.Generic;

namespace TheHub.Core.AI;

public enum AIPermissionLevel
{
    Level0_Safe = 0,        // Read-only (CPU, RAM, Time, Status) -> Auto-executed
    Level1_LowRisk = 1,     // Low-impact environment changes (Volume, Wallpaper, Workspace) -> Auto-executed with audit
    Level2_Elevated = 2,    // Elevated / App execution (Launch app, terminate app) -> Requires explicit confirmation
    Level3_Dangerous = 3    // System-critical / destructive actions -> Blocked by default
}

public enum AIAssistantState
{
    Idle,
    Processing,
    AwaitingConfirmation,
    Executing,
    DisabledByKillSwitch
}

public record AIAuditEntry(
    DateTime Timestamp,
    string Intent,
    AIPermissionLevel Level,
    string Status,
    Dictionary<string, string>? Parameters,
    string? Reason = null
);

public record AIToolIntent(
    string ToolName,
    AIPermissionLevel Level,
    Dictionary<string, string> Arguments,
    string RawPrompt
);

public record AIToolExecutionResult(
    bool Success,
    string Message,
    object? Data = null
);
