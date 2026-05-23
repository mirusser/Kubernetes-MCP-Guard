namespace InfraGate.Observer.Mcp;

internal static class ToolWhitelist
{
    public static void AssertAllowed(string toolName)
    {
        if (!ObserverConventions.ToolNames.ReadOnlyToolNames.Contains(toolName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' is not in the Observer's read-only whitelist. " +
                $"Allowed tools: {string.Join(", ", ObserverConventions.ToolNames.ReadOnlyToolNames)}");
        }
    }
}
