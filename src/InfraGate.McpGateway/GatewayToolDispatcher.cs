using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Notifications;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

internal sealed class GatewayToolDispatcher : IGatewayToolDispatcher
{
    private readonly DownstreamToolRegistry registry;
    private readonly GuardedToolRunner guardedRunner;
    private readonly IDomainAdapter domainAdapter;
    private readonly IGatewayApprovalService approvals;
    private readonly ApprovalStore approvalStore;
    private readonly IApprovalPreExecutionGate preExecutionGate;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ISubscriptionRegistry subscriptionRegistry;
    private readonly ILogger<GatewayToolDispatcher> logger;

    public GatewayToolDispatcher(
        DownstreamToolRegistry registry,
        GuardedToolRunner guardedRunner,
        IDomainAdapter domainAdapter,
        IGatewayApprovalService approvals,
        ApprovalStore approvalStore,
        IApprovalPreExecutionGate preExecutionGate,
        IHttpContextAccessor httpContextAccessor,
        ISubscriptionRegistry subscriptionRegistry,
        ILogger<GatewayToolDispatcher> logger)
    {
        this.registry = registry;
        this.guardedRunner = guardedRunner;
        this.domainAdapter = domainAdapter;
        this.approvals = approvals;
        this.approvalStore = approvalStore;
        this.preExecutionGate = preExecutionGate;
        this.httpContextAccessor = httpContextAccessor;
        this.subscriptionRegistry = subscriptionRegistry;
        this.logger = logger;
    }

    private string? CurrentSessionId =>
        httpContextAccessor.HttpContext?.Items[NotificationsConventions.McpSessionIdItemKey] as string;

    public async Task<ListToolsResult> ListToolsAsync(
        ListToolsRequestParams request,
        CancellationToken ct)
    {
        var tools = new List<Tool>();

        var readOnly = await registry.GetReadOnlyAsync(ct).ConfigureAwait(false);
        foreach (var dt in readOnly)
        {
            tools.Add(CreateForwardedTool(dt));
        }

        var destructive = await registry.GetDestructiveAsync(ct).ConfigureAwait(false);
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
        tools.Add(CreateGetPlanStatusTool());

        return new ListToolsResult { Tools = tools };
    }

    public async Task<CallToolResult> CallToolAsync(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        string toolName = request.Name;

        if (toolName.Equals(McpGatewayConventions.ToolNames.ApplyApprovedPlan, StringComparison.Ordinal))
        {
            return await HandleApplyApprovedPlanAsync(request, ct).ConfigureAwait(false);
        }

        if (toolName.Equals(McpGatewayConventions.ToolNames.GetPlanStatus, StringComparison.Ordinal))
        {
            return await HandleGetPlanStatusAsync(request, ct).ConfigureAwait(false);
        }

        if (toolName.StartsWith(McpGatewayConventions.ToolNames.RequestToolPrefix, StringComparison.Ordinal))
        {
            return await HandleRequestMutationAsync(toolName, request, ct).ConfigureAwait(false);
        }

        var readOnlyTools = await registry.GetReadOnlyAsync(ct).ConfigureAwait(false);
        if (readOnlyTools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal)))
        {
            return await HandleReadOnlyAsync(toolName, request, ct).ConfigureAwait(false);
        }

        var destructiveTools = await registry.GetDestructiveAsync(ct).ConfigureAwait(false);
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
        var result = await guardedRunner.CallAsync(toolName, arguments, ct).ConfigureAwait(false);

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

        var sessionId = CurrentSessionId;
        if (sessionId is not null)
        {
            subscriptionRegistry.BindSubject(sessionId, identity.Subject);
        }

        var args = ConvertArguments(request.Arguments);
        bool requestHasFindings = await guardedRunner.AuditRequestAsync(toolName, args, ct).ConfigureAwait(false);

        var planResult = await domainAdapter.BuildAsync(
            mutationToolName,
            args,
            new PlanRequester(identity.Subject, identity.AuthenticationType),
            ct).ConfigureAwait(false);

        if (!planResult.Succeeded || planResult.Envelope is null)
        {
            logger.LogWarning(
                "Plan build failed for tool '{ToolName}' by requester '{Requester}': {Message}",
                mutationToolName,
                identity.Subject,
                planResult.Message);

            if (planResult.Audit is { } audit)
            {
                await WritePlanAuditAsync(audit, mutationToolName, ct).ConfigureAwait(false);
            }

            var sanitized = await guardedRunner.SanitizeAndAuditResponseAsync(toolName, args, planResult.Message, ct).ConfigureAwait(false);
            var errorText = requestHasFindings || sanitized.HasFindings
                ? GuardedToolRunner.FormatWarningResponse(sanitized.Text)
                : sanitized.Text;

            return ErrorResult(errorText);
        }

        await approvalStore.CreatePlanAsync(
            planResult.Envelope,
            planResult.TargetNamespace,
            ct).ConfigureAwait(false);

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

        var gate = await approvals.EnsureApprovedOrCreateChallengeAsync(planId, ct).ConfigureAwait(false);
        if (!gate.IsApproved)
        {
            return HandleUnapprovedGate(gate, planId);
        }

        var preExecution = await preExecutionGate.EvaluateAsync(planId, domainAdapter, ct).ConfigureAwait(false);
        if (!preExecution.IsPassed || preExecution.Envelope is null || preExecution.Grant is null)
        {
            if (preExecution.Audit is { } audit)
            {
                await WritePlanAuditAsync(audit, planId, ct).ConfigureAwait(false);
            }

            return ErrorResult(preExecution.Message);
        }

        return await ExecutePlanAsync(planId, preExecution.Envelope, preExecution.Grant, ct).ConfigureAwait(false);
    }

    private async Task<CallToolResult> HandleGetPlanStatusAsync(
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

        var result = await approvalStore.GetPlanStatusAsync(planId, ct).ConfigureAwait(false);
        var response = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [McpGatewayConventions.ToolArguments.PlanId] = planId,
            [McpGatewayConventions.ToolResponseFields.Status] = ToPlanStatusValue(result.Status)
        };
        var json = JsonSerializer.Serialize(response);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }]
        };
    }

    private CallToolResult HandleUnapprovedGate(ApprovalGateResult gate, string planId)
    {
        if (gate.Status is ApprovalGateStatus.ApprovalRequired)
        {
            var sessionId = CurrentSessionId;
            if (sessionId is not null)
            {
                subscriptionRegistry.SubscribeToPlan(sessionId, planId);
            }
        }

        return gate.Status switch
        {
            ApprovalGateStatus.ApprovalRequired => new CallToolResult
            {
                Content = [new TextContentBlock { Text = gate.Message }]
            },
            ApprovalGateStatus.Refused => ErrorResult(gate.Message),
            _ => ErrorResult(gate.Message)
        };
    }

    private async Task<CallToolResult> ExecutePlanAsync(
        string planId,
        PlanEnvelope envelope,
        ApprovalGrant grant,
        CancellationToken ct)
    {
        DomainPlanExecutionResult executeResult;
        try
        {
            executeResult = await domainAdapter.ExecuteAsync(envelope, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = $"Plan '{planId}' execution failed: {ex.Message}";
            await WritePlanAuditAsync(
                new PlanAudit(
                    ApprovalConventions.AuditEvents.ApplyFailed,
                    new ApplyFailedPayload(
                        planId,
                        envelope.Operation,
                        message)),
                planId,
                ct).ConfigureAwait(false);

            logger.LogWarning(ex, "Approved plan {PlanId} execution failed.", planId);

            return ErrorResult(message);
        }

        if (!executeResult.IsSuccessful)
        {
            if (executeResult.Audit is { } audit)
            {
                await WritePlanAuditAsync(audit, planId, ct).ConfigureAwait(false);
            }
            else
            {
                await approvalStore.WriteAuditAsync(
                    ApprovalConventions.AuditEvents.ApplyDenied,
                    new ApplyDeniedPayload(planId, executeResult.Message),
                    ct).ConfigureAwait(false);
            }

            return ErrorResult(executeResult.Message);
        }

        await approvalStore.MarkAppliedAsync(
            envelope,
            executeResult.TargetNamespace ?? GetNamespaceFromEnvelope(envelope),
            grant,
            ct).ConfigureAwait(false);

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

    private static readonly string[] ApplyApprovedPlanRequiredArgs = ["planId"];

    private static readonly string[] GetPlanStatusRequiredArgs = [McpGatewayConventions.ToolArguments.PlanId];

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
                required = ApplyApprovedPlanRequiredArgs
            })
        };
    }

    private static Tool CreateGetPlanStatusTool()
    {
        return new Tool
        {
            Name = McpGatewayConventions.ToolNames.GetPlanStatus,
            Description = "Returns the current status of a pending approval plan (" +
                          ApprovalConventions.PlanStatusValues.NotFound + " | " +
                          ApprovalConventions.PlanStatusValues.ApprovalRequired + " | " +
                          ApprovalConventions.PlanStatusValues.Approved + " | " +
                          ApprovalConventions.PlanStatusValues.Applied + " | " +
                          ApprovalConventions.PlanStatusValues.Expired + "). " +
                          "Call this in a polling loop after " +
                          McpGatewayConventions.ToolNames.ApplyApprovedPlan +
                          " returns ApprovalRequired. When status is " +
                          ApprovalConventions.PlanStatusValues.Approved + ", call " +
                          McpGatewayConventions.ToolNames.ApplyApprovedPlan +
                          " to apply the plan. When status is " +
                          ApprovalConventions.PlanStatusValues.Expired + ", call " +
                          McpGatewayConventions.ToolNames.ApplyApprovedPlan +
                          " to create a new approval challenge.",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    planId = new { type = "string", description = "PlanId returned by one of the request_* tools." }
                },
                required = GetPlanStatusRequiredArgs
            })
        };
    }

    private static string ToPlanStatusValue(PlanStatus status) =>
        status switch
        {
            PlanStatus.NotFound => ApprovalConventions.PlanStatusValues.NotFound,
            PlanStatus.ApprovalRequired => ApprovalConventions.PlanStatusValues.ApprovalRequired,
            PlanStatus.Approved => ApprovalConventions.PlanStatusValues.Approved,
            PlanStatus.Applied => ApprovalConventions.PlanStatusValues.Applied,
            PlanStatus.Expired => ApprovalConventions.PlanStatusValues.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    private static IReadOnlyDictionary<string, object?> ConvertArguments(IDictionary<string, JsonElement>? args)
    {
        if (args is null || args.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, object?>(args.Count, StringComparer.Ordinal);
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
            await approvalStore.WriteAuditAsync(audit.EventName, audit.Payload, ct).ConfigureAwait(false);
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
