namespace InfraGate.McpGateway;

internal static class GuardrailAuditEventFactory
{
    public static GuardrailAuditEvent SensitiveData(
        string toolName,
        string? planId,
        string? subject,
        string? authenticationType,
        string identityKind,
        RedactionResult redacted)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpGatewayConventions.GuardrailAudit.EntryFields.RedactionPatterns] = redacted.PatternsMatched.ToArray(),
            [McpGatewayConventions.GuardrailAudit.EntryFields.RedactionCount] = redacted.CountByPattern
        };

        return new GuardrailAuditEvent(
            toolName,
            McpGatewayConventions.GuardrailAudit.ResponseDirection,
            McpGatewayConventions.GuardrailAudit.RedactSensitiveDataAction,
            [McpGatewayConventions.GuardrailCategories.SensitiveData],
            planId,
            subject,
            authenticationType,
            identityKind,
            metadata);
    }
}
