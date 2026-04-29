using System.Text.Json;

namespace InfraGate.McpGateway;

public interface IGuardrailAuditStore
{
    Task WriteAsync(GuardrailAuditEvent auditEvent, CancellationToken cancellationToken);
}

public sealed record GuardrailAuditEvent(
    string ToolName,
    string Direction,
    string Action,
    string[] Categories,
    string? PlanId,
    string? Subject,
    string? AuthenticationType);

public sealed class GuardrailAuditStore(McpGatewayOptions options) : IGuardrailAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public async Task WriteAsync(GuardrailAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.GuardAuditRoot);
        var path = Path.Combine(options.GuardAuditRoot, McpGatewayConventions.Paths.AuditFileName);
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            toolName = auditEvent.ToolName,
            direction = auditEvent.Direction,
            action = auditEvent.Action,
            categories = auditEvent.Categories,
            planId = auditEvent.PlanId,
            subject = auditEvent.Subject,
            authenticationType = auditEvent.AuthenticationType
        };
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line, cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }
}
