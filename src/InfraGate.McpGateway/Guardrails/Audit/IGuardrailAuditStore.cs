namespace InfraGate.McpGateway;

public interface IGuardrailAuditStore
{
    Task WriteAsync(GuardrailAuditEvent auditEvent, CancellationToken cancellationToken);
}
