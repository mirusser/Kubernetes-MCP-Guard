using System.Collections.Generic;
using System.Text.Json;

namespace InfraGate.McpGateway;

public sealed class GuardrailAuditStore(McpGatewayOptions options) : IGuardrailAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public async Task WriteAsync(GuardrailAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.GuardAuditRoot);
        string path = Path.Combine(options.GuardAuditRoot, McpGatewayConventions.Paths.AuditFileName);
        var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpGatewayConventions.GuardrailAudit.EntryFields.Timestamp] = DateTimeOffset.UtcNow,
            [McpGatewayConventions.GuardrailAudit.EntryFields.ToolName] = auditEvent.ToolName,
            [McpGatewayConventions.GuardrailAudit.EntryFields.Direction] = auditEvent.Direction,
            [McpGatewayConventions.GuardrailAudit.EntryFields.Action] = auditEvent.Action,
            [McpGatewayConventions.GuardrailAudit.EntryFields.Categories] = auditEvent.Categories,
            [McpGatewayConventions.GuardrailAudit.EntryFields.PlanId] = auditEvent.PlanId,
            [McpGatewayConventions.GuardrailAudit.EntryFields.Subject] = auditEvent.Subject,
            [McpGatewayConventions.GuardrailAudit.EntryFields.AuthenticationType] = auditEvent.AuthenticationType,
            [McpGatewayConventions.GuardrailAudit.EntryFields.IdentityKind] = auditEvent.IdentityKind
        };
        string line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }
}
