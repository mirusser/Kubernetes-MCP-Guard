namespace InfraGate.Executor.Mcp;

internal static class ExecutorToolWhitelist
{
    public static void AssertAllowed(string toolName)
    {
        if (!ExecutorConventions.ToolNames.AllowedToolNames.Contains(toolName))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' is not in the Executor's allowed whitelist. " +
                $"Allowed tools: {string.Join(", ", ExecutorConventions.ToolNames.AllowedToolNames)}");
        }
    }
}
