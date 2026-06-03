using A2A;
using InfraGate.AgentGuardrails;
using InfraGate.AgentMcp;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Diagnostics;
using ModelContextProtocol.Protocol;

namespace InfraGate.Observer.Handoff;

#pragma warning disable MEAI001 // IAgentHandler is in experimental A2A package
internal sealed class ObserverInboundAgentHandler(
    ILogger<ObserverInboundAgentHandler> logger,
    IObserverAuditOutbox? auditOutbox = null,
    IAgentMcpToolset? mcpToolset = null,
    AgentGuardrailPolicy? guardrailPolicy = null) : IAgentHandler
{
    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        string? json = context.Message?.Parts.FirstOrDefault(p => p.Text is not null)?.Text;

        ObserverInboundEnvelope? envelope = null;
        if (json is not null)
        {
            try { envelope = JsonSerializer.Deserialize<ObserverInboundEnvelope>(json); }
            catch (JsonException) { /* envelope remains null, handled below */ }
        }

        string response;
        if (envelope is null)
        {
            response = "error: malformed request";
        }
        else if (string.Equals(envelope.Intent, ObserverInboundIntents.ToolRequest, StringComparison.Ordinal))
        {
            response = await HandleToolRequestAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ObserverLogEvents.LogInboundUnknownIntent(logger, envelope.Intent);
            response = "error: unknown intent";
        }

        await eventQueue.EnqueueMessageAsync(
            new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = A2A.Role.Agent,
                Parts = [new Part { Text = response }],
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task CancelAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken) => Task.CompletedTask;

#pragma warning disable S3776 // linear guard-chain; complexity comes from essential multi-guard dispatch
    private async Task<string> HandleToolRequestAsync(
        ObserverInboundEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = envelope.ToolRequest;
        if (request is null)
            return ErrorResponse("error: missing tool request payload");

        bool allowed = guardrailPolicy is not null &&
                       guardrailPolicy.AllowedToolNames.Contains(request.ToolName);

        if (!allowed)
        {
            ObserverLogEvents.LogInboundToolDenied(logger, request.ToolName);
            await AppendAuditAsync(
                new ObserverAuditEntry(
                    EventName: ObserverAuditEvents.HandoffToolDenied,
                    Payload: new { toolName = request.ToolName },
                    CycleId: envelope.CycleId,
                    ActorSubject: ObserverAuditEvents.Subjects.Planner,
                    Outcome: ObserverAuditEvents.Outcomes.Denied),
                cancellationToken).ConfigureAwait(false);

            return ErrorResponse("error: tool not permitted by observer whitelist");
        }

        if (mcpToolset is null)
            return ErrorResponse("error: MCP toolset unavailable");

        IReadOnlyDictionary<string, object?>? args = null;
        if (request.ArgumentsJson is not null)
        {
            try
            {
                args = JsonSerializer.Deserialize<Dictionary<string, object?>>(request.ArgumentsJson);
            }
            catch (JsonException ex)
            {
                return ErrorResponse($"error: malformed arguments json - {ex.Message}");
            }
        }

        CallToolResult result;
        try
        {
            result = await mcpToolset.CallToolAsync(request.ToolName, args, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ObserverLogEvents.LogInboundToolCallFailed(logger, request.ToolName, ex);
            return ErrorResponse($"error: tool execution failed - {ex.Message}");
        }

        return await BuildToolResultResponseAsync(result, request.ToolName, envelope.CycleId, cancellationToken).ConfigureAwait(false);
    }
#pragma warning restore S3776

    private async Task<string> BuildToolResultResponseAsync(
        CallToolResult result,
        string toolName,
        string? cycleId,
        CancellationToken cancellationToken)
    {
        bool isError = result.IsError == true;
        string resultText = result.Content is not null
            ? string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text))
            : string.Empty;

        if (!isError)
            ObserverLogEvents.LogInboundToolServed(logger, toolName, cycleId ?? string.Empty);
        else
            ObserverLogEvents.LogMcpToolError(logger, toolName);

        await AppendAuditAsync(
            new ObserverAuditEntry(
                EventName: ObserverAuditEvents.HandoffToolServed,
                Payload: new { toolName, isError },
                CycleId: cycleId,
                ActorSubject: ObserverAuditEvents.Subjects.Planner,
                Outcome: isError ? ObserverAuditEvents.Outcomes.ToolError : ObserverAuditEvents.Outcomes.Served),
            cancellationToken).ConfigureAwait(false);

        var payload = new ToolResponsePayload { IsError = isError, ResultJson = resultText };
        return JsonSerializer.Serialize(payload);
    }

    private static string ErrorResponse(string message)
        => JsonSerializer.Serialize(new ToolResponsePayload { IsError = true, ResultJson = message });

    private async ValueTask AppendAuditAsync(ObserverAuditEntry entry, CancellationToken ct)
    {
        if (auditOutbox is not null)
            await auditOutbox.AppendAsync(entry, ct).ConfigureAwait(false);
    }
}
#pragma warning restore MEAI001
