namespace InfraGate.Planner.Mcp;

internal static class PlannerToolWhitelist
{
    public static void AssertAllowed(string toolName)
    {
        if (!PlannerConventions.ToolNames.AllowedToolNames.Contains(toolName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' is not in the Planner's allowed whitelist. " +
                $"Allowed tools: {string.Join(", ", PlannerConventions.ToolNames.AllowedToolNames)}");
        }
    }
}
