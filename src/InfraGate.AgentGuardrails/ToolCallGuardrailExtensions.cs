namespace InfraGate.AgentGuardrails;

public static class ToolCallGuardrailExtensions
{
    // Default behavior is block-and-continue: a blocked call returns a refusal string
    // but does NOT set context.Terminate, so the agent can still finish with allowed tools.
    public static AIAgentBuilder UseToolCallGuardrail(
        this AIAgentBuilder builder,
        AgentGuardrailPolicy policy,
        AgentGuardrailMetrics guardrailMetrics,
        string agentName)
    {
        return builder.Use(async (innerAgent, context, next, cancellationToken) =>
        {
            string toolName = context.Function?.Name ?? string.Empty;

            if (policy.AllowedToolNames.Contains(toolName))
            {
                return await next(context, cancellationToken).ConfigureAwait(false);
            }

            guardrailMetrics.RecordToolBlocked(agentName, toolName, AgentGuardrailConventions.Reasons.ToolNotAllowed);
            return $"[BLOCKED] Tool '{toolName}' is not in the allow-list for agent '{agentName}'.";
        });
    }
}
