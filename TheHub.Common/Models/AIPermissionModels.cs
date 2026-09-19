namespace TheHub.Common.Models;

public enum AIPermissionLevel
{
    Level0_Safe = 0,       // Open apps, read system info, search, change widget (Direct execution)
    Level1_Moderate = 1,   // Close apps, adjust volume/brightness, change wallpaper (Executes with audit log)
    Level2_Sensitive = 2,  // Delete files, change system config, execute scripts (Requires explicit prompt confirmation)
    Level3_Dangerous = 3   // Admin scripts, mass wipe, install packages (Disabled by default)
}

public class AIPermissionSetting
{
    public string ActionName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AIPermissionLevel Level { get; set; }
    public bool IsGranted { get; set; } = true;
}

public class AIAuditEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserPrompt { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = string.Empty;
    public AIPermissionLevel Level { get; set; }
    public bool Approved { get; set; }
    public string ExecutionResult { get; set; } = string.Empty;
}
