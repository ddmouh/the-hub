using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TheHub.Core.AI;

public interface IAIModelClient
{
    Task<AIToolIntent?> ParsePromptAsync(string prompt, CancellationToken ct = default);
}

public class AIIntentParser
{
    private readonly IAIModelClient? _modelClient;

    public AIIntentParser(IAIModelClient? modelClient = null)
    {
        _modelClient = modelClient;
    }

    public async Task<AIToolIntent> ParseAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new AIToolIntent("unknown", AIPermissionLevel.Level0_Safe, new(), prompt);
        }

        var raw = prompt.Trim();

        // Level 3 Dangerous detection
        if (Regex.IsMatch(raw, @"\b(delete|format|rmdir|rm -rf|del \/s|drop database|powershell -c|cmd \/c)\b", RegexOptions.IgnoreCase))
        {
            return new AIToolIntent("execute_system_script", AIPermissionLevel.Level3_Dangerous,
                new Dictionary<string, string> { { "command", raw } }, raw);
        }

        // Level 0 Metrics
        if (Regex.IsMatch(raw, @"\b(cpu|ram|memory|metrics|hardware|stats|system load)\b", RegexOptions.IgnoreCase))
        {
            return new AIToolIntent("read_metrics", AIPermissionLevel.Level0_Safe, new(), raw);
        }

        // Level 1 Volume
        var volMatch = Regex.Match(raw, @"\b(?:set\s+)?volume\s+(?:to\s+)?(\d{1,3})%?", RegexOptions.IgnoreCase);
        if (volMatch.Success)
        {
            return new AIToolIntent("set_volume", AIPermissionLevel.Level1_LowRisk,
                new Dictionary<string, string> { { "volume", volMatch.Groups[1].Value } }, raw);
        }

        // Level 1 Workspace
        var wsMatch = Regex.Match(raw, @"\b(?:switch|activate|change)\s+(?:workspace\s+to\s+|to\s+workspace\s+|to\s+)([\w\s\-]+)", RegexOptions.IgnoreCase);
        if (wsMatch.Success)
        {
            var wsName = Regex.Replace(wsMatch.Groups[1].Value, @"\bworkspace\b", "", RegexOptions.IgnoreCase).Trim();
            return new AIToolIntent("switch_workspace", AIPermissionLevel.Level1_LowRisk,
                new Dictionary<string, string> { { "workspace", wsName } }, raw);
        }

        // Level 1 Wallpaper
        var wallMatch = Regex.Match(raw, @"\b(?:set|change)\s+wallpaper\s+(?:to\s+)?(.+)", RegexOptions.IgnoreCase);
        if (wallMatch.Success)
        {
            return new AIToolIntent("set_wallpaper", AIPermissionLevel.Level1_LowRisk,
                new Dictionary<string, string> { { "path", wallMatch.Groups[1].Value.Trim() } }, raw);
        }

        // Level 2 Launch app
        var launchMatch = Regex.Match(raw, @"\b(?:open|launch|start|run)\s+([a-zA-Z0-9_\-\.\s]+)", RegexOptions.IgnoreCase);
        if (launchMatch.Success)
        {
            return new AIToolIntent("launch_app", AIPermissionLevel.Level2_Elevated,
                new Dictionary<string, string> { { "app", launchMatch.Groups[1].Value.Trim() } }, raw);
        }

        // Level 2 Close app
        var closeMatch = Regex.Match(raw, @"\b(?:close|kill|terminate|stop)\s+([a-zA-Z0-9_\-\.\s]+)", RegexOptions.IgnoreCase);
        if (closeMatch.Success)
        {
            return new AIToolIntent("close_app", AIPermissionLevel.Level2_Elevated,
                new Dictionary<string, string> { { "process", closeMatch.Groups[1].Value.Trim() } }, raw);
        }

        // Model client fallback
        if (_modelClient != null)
        {
            try
            {
                var clientIntent = await _modelClient.ParsePromptAsync(prompt, ct);
                if (clientIntent != null) return clientIntent;
            }
            catch
            {
            }
        }

        return new AIToolIntent("unknown", AIPermissionLevel.Level0_Safe, new(), prompt);
    }
}
