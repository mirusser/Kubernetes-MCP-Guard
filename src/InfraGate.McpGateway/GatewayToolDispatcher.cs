using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.McpGateway.Auth;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpGateway;

public sealed class GatewayToolDispatcher : IGatewayToolDispatcher
{
    private readonly DownstreamToolRegistry registry;
    private readonly GuardedToolRunner guardedRunner;
    private readonly IDomainAdapter domainAdapter;
    private readonly IGatewayApprovalService approvals;
    private readonly ApprovalStore approvalStore;
    private readonly IApprovalPreExecutionGate preExecutionGate;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ILogger<GatewayToolDispatcher> logger;

    public GatewayToolDispatcher(
        DownstreamToolRegistry registry,
        GuardedToolRunner guardedRunner,
        IDomainAdapter domainAdapter,
        IGatewayApprovalService approvals,
        ApprovalStore approvalStore,
        IApprovalPreExecutionGate preExecutionGate,
        IHttpContextAccessor httpContextAccessor,
        ILogger<GatewayToolDispatcher> logger)
    {
        this.registry = registry;
        this.guardedRunner = guardedRunner;
        this.domainAdapter = domainAdapter;
        this.approvals = approvals;
        this.approvalStore = approvalStore;
        this.preExecutionGate = preExecutionGate;
        this.httpContextAccessor = httpContextAccessor;
        this.logger = logger;
    }

    public async Task<ListToolsResult> ListToolsAsync(
        ListToolsRequestParams request,
        CancellationToken ct)
    {
        var tools = new List<Tool>();

        var readOnly = await registry.GetReadOnlyAsync(ct);
        foreach (var dt in readOnly)
        {
            tools.Add(CreateForwardedTool(dt));
        }

        var destructive = await registry.GetDestructiveAsync(ct);
        foreach (var dt in destructive)
        {
            var requestName = McpGatewayConventions.ToolNames.RequestToolPrefix + dt.Name;
            var requestDescription = $"Creates a pending approval plan for '{dt.Name}'. {dt.Description}";

            tools.Add(new Tool
            {
                Name = requestName,
                Description = requestDescription,
                InputSchema = dt.InputSchema
            });
        }

        tools.Add(CreateApplyApprovedPlanTool());

        return new ListToolsResult { Tools = tools };
    }

    public async Task<CallToolResult> CallToolAsync(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        string toolName = request.Name;

        if (toolName == McpGatewayConventions.ToolNames.ApplyApprovedPlan)
        {
            return await HandleApplyApprovedPlanAsync(request, ct);
        }

        if (toolName.StartsWith(McpGatewayConventions.ToolNames.RequestToolPrefix, StringComparison.Ordinal))
        {
            return await HandleRequestMutationAsync(toolName, request, ct);
        }

        var readOnlyTools = await registry.GetReadOnlyAsync(ct);
        if (readOnlyTools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal)))
        {
            return await HandleReadOnlyAsync(toolName, request, ct);
        }

        var destructiveTools = await registry.GetDestructiveAsync(ct);
        if (destructiveTools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal)))
        {
            return ErrorResult(
                $"Refused: destructive tool '{toolName}' must be requested through " +
                $"'{McpGatewayConventions.ToolNames.RequestToolPrefix}{toolName}' and executed with " +
                $"{McpGatewayConventions.ToolNames.ApplyApprovedPlan}.");
        }

        return ErrorResult($"Unknown tool '{toolName}'.");
    }

    private async Task<CallToolResult> HandleReadOnlyAsync(
        string toolName,
        CallToolRequestParams request,
        CancellationToken ct)
    {
        var arguments = ConvertArguments(request.Arguments);
        var result = await guardedRunner.CallAsync(toolName, arguments, ct);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result }]
        };
    }

    private async Task<CallToolResult> HandleRequestMutationAsync(
        string toolName,
        CallToolRequestParams request,
        CancellationToken ct)
    {
        var mutationToolName = toolName.Substring(McpGatewayConventions.ToolNames.RequestToolPrefix.Length);

        var identity = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (identity is null)
        {
            return ErrorResult("Refused: mutation plan creation requires an authenticated OAuth subject.");
        }

        var args = ConvertArguments(request.Arguments);
        bool requestHasFindings = await guardedRunner.AuditRequestAsync(toolName, args, ct);

        var planResult = await domainAdapter.BuildAsync(
            mutationToolName,
            args,
            new PlanRequester(identity.Subject, identity.AuthenticationType),
            ct);

        if (!planResult.Succeeded || planResult.Envelope is null)
        {
            logger.LogWarning(
                "Plan build failed for tool '{ToolName}' by requester '{Requester}': {Message}",
                mutationToolName,
                identity.Subject,
                planResult.Message);

            if (planResult.Audit is { } audit)
            {
                await WritePlanAuditAsync(audit, mutationToolName, ct);
            }

            var sanitized = await guardedRunner.SanitizeAndAuditResponseAsync(toolName, args, planResult.Message, ct);
            var errorText = requestHasFindings || sanitized.HasFindings
                ? GuardedToolRunner.FormatWarningResponse(sanitized.Text)
                : sanitized.Text;

            return ErrorResult(errorText);
        }

        await approvalStore.CreatePlanAsync(
            planResult.Envelope,
            planResult.TargetNamespace,
            ct);

        var message = $"Approval plan '{planResult.PlanId}' created. To execute, submit with execute_approved_plan(planId=\"{planResult.PlanId}\").";
        if (requestHasFindings)
        {
            message = GuardedToolRunner.FormatWarningResponse(message);
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }]
        };
    }

    private async Task<CallToolResult> HandleApplyApprovedPlanAsync(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        var args = ConvertArguments(request.Arguments);
        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.PlanId, out var planIdObj) ||
            planIdObj is not string planId ||
            string.IsNullOrWhiteSpace(planId))
        {
            return ErrorResult("Missing required argument: planId.");
        }

        var gate = await approvals.EnsureApprovedOrCreateChallengeAsync(planId, ct);
        if (!gate.IsApproved)
        {
            return gate.Message.Contains("Refused:", StringComparison.Ordinal)
                ? ErrorResult(gate.Message)
                : new CallToolResult { Content = [new TextContentBlock { Text = gate.Message }] };
        }

        var preExecution = await preExecutionGate.EvaluateAsync(planId, domainAdapter, ct);
        if (!preExecution.IsPassed || preExecution.Envelope is null || preExecution.Grant is null)
        {
            if (preExecution.Audit is { } audit)
            {
                await WritePlanAuditAsync(audit, planId, ct);
            }

            return ErrorResult(preExecution.Message);
        }

        DomainPlanExecutionResult executeResult;
        try
        {
            executeResult = await domainAdapter.ExecuteAsync(preExecution.Envelope, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = $"Plan '{planId}' execution failed: {ex.Message}";
            await WritePlanAuditAsync(
                new PlanAudit(
                    ApprovalConventions.AuditEvents.ApplyFailed,
                    new ApplyFailedPayload(
                        planId,
                        preExecution.Envelope.Operation,
                        message)),
                planId,
                ct);

            logger.LogWarning(ex, "Approved plan {PlanId} execution failed.", planId);

            return ErrorResult(message);
        }

        if (!executeResult.IsSuccessful)
        {
            if (executeResult.Audit is { } audit)
            {
                await WritePlanAuditAsync(audit, planId, ct);
            }
            else
            {
                await approvalStore.WriteAuditAsync(
                    ApprovalConventions.AuditEvents.ApplyDenied,
                    new ApplyDeniedPayload(planId, executeResult.Message),
                    ct);
            }

            return ErrorResult(executeResult.Message);
        }

        await approvalStore.MarkAppliedAsync(
            preExecution.Envelope,
            executeResult.TargetNamespace ?? GetNamespaceFromEnvelope(preExecution.Envelope),
            preExecution.Grant,
            ct);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = executeResult.Message }]
        };
    }

    private static Tool CreateForwardedTool(DownstreamTool dt)
    {
        return new Tool
        {
            Name = dt.Name,
            Description = dt.Description,
            InputSchema = dt.InputSchema
        };
    }

    private static Tool CreateApplyApprovedPlanTool()
    {
        return new Tool
        {
            Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            Description = "Returns a browser approval URL for a pending plan, or applies it after out-of-band approval.",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    planId = new { type = "string", description = "PlanId returned by one of the request_* tools." }
                },
                required = new[] { "planId" }
            })
        };
    }

    private static IReadOnlyDictionary<string, object?> ConvertArguments(IDictionary<string, JsonElement>? args)
    {
        if (args is null || args.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>(args.Count);
        foreach (var (key, element) in args)
        {
            result[key] = JsonElementToObject(element);
        }

        return result;
    }

    private static object? JsonElementToObject(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out int i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element,
            _ => element
        };

    private async Task WritePlanAuditAsync(PlanAudit audit, string context, CancellationToken ct)
    {
        try
        {
            await approvalStore.WriteAuditAsync(audit.EventName, audit.Payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to write audit event {EventName} for {Context}.",
                audit.EventName,
                context);
        }
    }

    private static string GetNamespaceFromEnvelope(PlanEnvelope envelope)
    {
        return envelope.AdapterId;
    }

    private static CallToolResult ErrorResult(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }]
    };
}
