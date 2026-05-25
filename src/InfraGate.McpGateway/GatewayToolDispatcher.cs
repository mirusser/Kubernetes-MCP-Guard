using System.Security.Claims;
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
    private const int WaitForPlanApprovalDefaultTimeoutSeconds = 55;
    private const int WaitForPlanApprovalMinimumTimeoutSeconds = 1;
    private const int WaitForPlanApprovalMaximumTimeoutSeconds = 300;

    private static readonly TimeSpan WaitForPlanApprovalPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly DownstreamToolRegistry registry;
    private readonly GuardedToolRunner guardedRunner;
    private readonly IDomainAdapter domainAdapter;
    private readonly IGatewayApprovalService approvals;
    private readonly IApprovalPlanWorkflow approvalPlans;
    private readonly IApprovalExecutionWorkflow approvalExecution;
    private readonly IApprovalAuditPublisher auditPublisher;
    private readonly IApprovalPreExecutionGate preExecutionGate;
    private readonly IProposePlanHandler proposePlanHandler;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ISubscriptionRegistry subscriptionRegistry;
    private readonly IGuardrailAuditStore auditStore;
    private readonly ILogger<GatewayToolDispatcher> logger;

    public GatewayToolDispatcher( // NOSONAR:S107 — 13-param DI constructor. Aggregates would add indirection; explicit DI surfaces failures immediately.
        DownstreamToolRegistry registry,
        GuardedToolRunner guardedRunner,
        IDomainAdapter domainAdapter,
        IGatewayApprovalService approvals,
        IApprovalPlanWorkflow approvalPlans,
        IApprovalExecutionWorkflow approvalExecution,
        IApprovalAuditPublisher auditPublisher,
        IApprovalPreExecutionGate preExecutionGate,
        IProposePlanHandler proposePlanHandler,
        IHttpContextAccessor httpContextAccessor,
        ISubscriptionRegistry subscriptionRegistry,
        IGuardrailAuditStore auditStore,
        ILogger<GatewayToolDispatcher> logger)
    {
        this.registry = registry;
        this.guardedRunner = guardedRunner;
        this.domainAdapter = domainAdapter;
        this.approvals = approvals;
        this.approvalPlans = approvalPlans;
        this.approvalExecution = approvalExecution;
        this.auditPublisher = auditPublisher;
        this.preExecutionGate = preExecutionGate;
        this.proposePlanHandler = proposePlanHandler;
        this.httpContextAccessor = httpContextAccessor;
        this.subscriptionRegistry = subscriptionRegistry;
        this.auditStore = auditStore;
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
        tools.Add(CreateProposePlanTool());
        tools.Add(CreateWaitForPlanApprovalTool());

        return new ListToolsResult { Tools = tools };
    }

    public async Task<CallToolResult> CallToolAsync(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        return await CallToolAsyncCore(request, ct).ConfigureAwait(false);
    }

    private async Task<CallToolResult> CallToolAsyncCore(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        string toolName = request.Name;

        if (toolName.Equals(McpGatewayConventions.ToolNames.ApplyApprovedPlan, StringComparison.Ordinal))
        {
            var scopeResult = await RequireAnyScopeAsync(
                toolName,
                McpGatewayConventions.ToolScopeRequirements.MutationScope,
                McpGatewayConventions.ToolScopeRequirements.ExecuteScope).ConfigureAwait(false);
            if (scopeResult is not null)
            {
                return scopeResult;
            }

            return await HandleApplyApprovedPlanAsync(request, ct).ConfigureAwait(false);
        }

        if (toolName.Equals(McpGatewayConventions.ToolNames.GetPlanStatus, StringComparison.Ordinal))
        {
            var scopeResult = await RequireMutationScopeAsync(toolName).ConfigureAwait(false);
            if (scopeResult is not null)
            {
                return scopeResult;
            }

            return await HandleGetPlanStatusAsync(request, ct).ConfigureAwait(false);
        }

        if (toolName.Equals(McpGatewayConventions.ToolNames.WaitForPlanApproval, StringComparison.Ordinal))
        {
            var scopeResult = await RequireAnyScopeAsync(
                toolName,
                McpGatewayConventions.ToolScopeRequirements.MutationScope,
                McpGatewayConventions.ToolScopeRequirements.ExecuteScope).ConfigureAwait(false);
            if (scopeResult is not null)
            {
                return scopeResult;
            }

            return await HandleWaitForPlanApprovalAsync(request, ct).ConfigureAwait(false);
        }

        if (toolName.Equals(McpGatewayConventions.ToolNames.ProposePlan, StringComparison.Ordinal))
        {
            var scopeResult = await RequireAnyScopeAsync(
                toolName,
                McpGatewayConventions.ToolScopeRequirements.MutationScope,
                McpGatewayConventions.ToolScopeRequirements.ProposeScope).ConfigureAwait(false);
            if (scopeResult is not null)
            {
                return scopeResult;
            }

            return await HandleProposePlanAsync(request, ct).ConfigureAwait(false);
        }

        if (toolName.StartsWith(McpGatewayConventions.ToolNames.RequestToolPrefix, StringComparison.Ordinal))
        {
            var scopeResult = await RequireMutationScopeAsync(toolName).ConfigureAwait(false);
            if (scopeResult is not null)
            {
                return scopeResult;
            }

            return await HandleRequestMutationAsync(toolName, request, ct).ConfigureAwait(false);
        }

        return await DispatchDownstreamToolAsync(toolName, request, ct).ConfigureAwait(false);
    }

    private async Task<CallToolResult> DispatchDownstreamToolAsync(
        string toolName, CallToolRequestParams request, CancellationToken ct)
    {
        if (await IsReadOnlyToolAsync(toolName, ct).ConfigureAwait(false))
        {
            return await HandleReadOnlyAsync(toolName, request, ct).ConfigureAwait(false);
        }

        if (await IsDestructiveToolAsync(toolName, ct).ConfigureAwait(false))
        {
            return ErrorResult(
                $"Refused: destructive tool '{toolName}' must be requested through " +
                $"'{McpGatewayConventions.ToolNames.RequestToolPrefix}{toolName}' and executed with " +
                $"{McpGatewayConventions.ToolNames.ApplyApprovedPlan}.");
        }

        return ErrorResult($"Unknown tool '{toolName}'.");
    }

    private async Task<bool> IsReadOnlyToolAsync(string toolName, CancellationToken ct)
    {
        var readOnlyTools = await registry.GetReadOnlyAsync(ct).ConfigureAwait(false);
        return readOnlyTools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
    }

    private async Task<bool> IsDestructiveToolAsync(string toolName, CancellationToken ct)
    {
        var destructiveTools = await registry.GetDestructiveAsync(ct).ConfigureAwait(false);
        return destructiveTools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
    }

    private async Task<CallToolResult> HandleReadOnlyAsync(
        string toolName,
        CallToolRequestParams request,
        CancellationToken ct)
    {
        var scopeResult = await RequireAnyToolScopeAsync(toolName).ConfigureAwait(false);
        if (scopeResult is not null)
        {
            return scopeResult;
        }

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

        var args = ConvertArguments(request.Arguments);
        bool requestHasFindings = await guardedRunner.AuditRequestAsync(toolName, args, ct).ConfigureAwait(false);

        var planResult = await domainAdapter.BuildAsync(
            mutationToolName,
            args,
            new PlanRequester(identity.Subject, identity.AuthenticationType),
            ApprovalPolicy.SameSubject(),
            ct).ConfigureAwait(false);

        if (!planResult.Succeeded || planResult.Envelope is null)
        {
            return await HandlePlanBuildFailureAsync(
                toolName, mutationToolName, args, identity, planResult, requestHasFindings, ct)
                .ConfigureAwait(false);
        }

        await approvalPlans.CreatePlanAsync(
            planResult.Envelope,
            planResult.TargetNamespace,
            ct).ConfigureAwait(false);

        if (sessionId is not null)
        {
            subscriptionRegistry.BindSubject(sessionId, identity.Subject);
            subscriptionRegistry.SubscribeToPlan(sessionId, planResult.PlanId);
        }

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

    private async Task<CallToolResult> HandlePlanBuildFailureAsync(
        string toolName,
        string mutationToolName,
        IReadOnlyDictionary<string, object?> args,
        GatewayApprovalIdentity identity,
        PlanBuildResult planResult,
        bool requestHasFindings,
        CancellationToken ct)
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

        var sanitized = await guardedRunner.SanitizeAndAuditResponseAsync(toolName, args, planResult.Message, ct)
            .ConfigureAwait(false);
        var errorText = requestHasFindings || sanitized.HasFindings
            ? GuardedToolRunner.FormatWarningResponse(sanitized.Text)
            : sanitized.Text;

        return ErrorResult(errorText);
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
            return HandleUnapprovedGate(gate);
        }

        var granted = await approvalPlans.GetGrantedPlanAsync(planId, ct).ConfigureAwait(false);
        if (!granted.IsGranted || granted.Grant is null)
        {
            return ErrorResult(granted.Message);
        }

        var beginExecution = await approvalExecution.BeginExecutionAttemptAsync(planId, granted.Grant, ct)
            .ConfigureAwait(false);
        if (!beginExecution.IsStarted || beginExecution.Attempt is null)
        {
            return ErrorResult(beginExecution.Message);
        }

        var preExecution = await preExecutionGate.EvaluateAsync(planId, domainAdapter, ct).ConfigureAwait(false);
        if (!preExecution.IsPassed || preExecution.Envelope is null || preExecution.Grant is null)
        {
            var audit = preExecution.Audit ?? new PlanAudit(
                ApprovalConventions.AuditEvents.ApplyDenied,
                new ApplyDeniedPayload(planId, preExecution.Message));
            await approvalExecution.RecordExecutionBlockedAsync(
                beginExecution.Attempt,
                preExecution.Message,
                preExecution.ReasonCode,
                audit,
                ct).ConfigureAwait(false);

            return ErrorResult(preExecution.Message);
        }

        return await ExecutePlanAsync(
            planId,
            preExecution.Envelope,
            preExecution.Grant,
            beginExecution.Attempt,
            ct).ConfigureAwait(false);
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

        var result = await approvalPlans.GetPlanStatusAsync(planId, ct).ConfigureAwait(false);
        var json = PlanStatusResponse.Serialize(planId, result.Status);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }]
        };
    }

    private async Task<CallToolResult> HandleWaitForPlanApprovalAsync(
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

        if (!TryGetWaitTimeoutSeconds(args, out int timeoutSeconds, out var timeoutError))
        {
            return ErrorResult(timeoutError);
        }

        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(timeoutSeconds);
        bool timedOut = false;
        PlanStatusResult result;
        do
        {
            result = await approvalPlans.GetPlanStatusAsync(planId, ct).ConfigureAwait(false);
            if (IsTerminalWaitStatus(result.Status))
            {
                break;
            }

            var now = TimeProvider.System.GetUtcNow();
            if (now >= deadline)
            {
                timedOut = true;
                break;
            }

            var delay = deadline - now;
            if (delay > WaitForPlanApprovalPollInterval)
            {
                delay = WaitForPlanApprovalPollInterval;
            }

            await Task.Delay(delay, TimeProvider.System, ct).ConfigureAwait(false);
        }
        while (true);

        var json = PlanStatusResponse.Serialize(planId, result.Status, timedOut);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }]
        };
    }

    private async Task<CallToolResult> HandleProposePlanAsync(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        var args = ConvertArguments(request.Arguments);
        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.OperationType, out var operationTypeObj) ||
            operationTypeObj is not string operationType ||
            string.IsNullOrWhiteSpace(operationType))
        {
            return ErrorResult("Missing required argument: operationType.");
        }

        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.OperationArguments, out var argumentsObj) ||
            argumentsObj is not JsonElement argumentsElement ||
            argumentsElement.ValueKind != JsonValueKind.Object)
        {
            return ErrorResult("Missing required argument: arguments.");
        }

        var operationArguments = ConvertObjectArguments(argumentsElement);
        return await proposePlanHandler.ProposeAsync(operationType, operationArguments, ct).ConfigureAwait(false);
    }

    private static CallToolResult HandleUnapprovedGate(ApprovalGateResult gate)
    {
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
        ExecutionAttempt attempt,
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
            var audit = new PlanAudit(
                ApprovalConventions.AuditEvents.ApplyFailed,
                new ApplyFailedPayload(
                    planId,
                    envelope.Operation,
                    message));
            await approvalExecution.RecordExecutionFailedAsync(
                attempt,
                message,
                reasonCode: null,
                audit,
                ct).ConfigureAwait(false);

            logger.LogWarning(ex, "Approved plan {PlanId} execution failed.", planId);

            return ErrorResult(message);
        }

        if (!executeResult.IsSuccessful)
        {
            var audit = executeResult.Audit ?? new PlanAudit(
                ApprovalConventions.AuditEvents.ApplyFailed,
                new ApplyFailedPayload(planId, envelope.Operation, executeResult.Message));
            await approvalExecution.RecordExecutionFailedAsync(
                attempt,
                executeResult.Message,
                executeResult.ReasonCode,
                audit,
                ct).ConfigureAwait(false);

            return ErrorResult(executeResult.Message);
        }

        string targetNamespace = executeResult.TargetNamespace ?? GetNamespaceFromEnvelope(envelope);
        await approvalExecution.RecordExecutionSucceededAsync(
            attempt,
            grant,
            targetNamespace,
            executeResult.Message,
            new PlanAudit(
                ApprovalConventions.AuditEvents.PlanApplied,
                new PlanAppliedPayload(envelope.Id, envelope.Operation, targetNamespace, grant.ReviewDigest.Value)),
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

    private static readonly string[] WaitForPlanApprovalRequiredArgs = [McpGatewayConventions.ToolArguments.PlanId];

    private static readonly string[] ProposePlanRequiredArgs =
    [
        McpGatewayConventions.ToolArguments.OperationType,
        McpGatewayConventions.ToolArguments.OperationArguments
    ];

    private static Tool CreateApplyApprovedPlanTool()
    {
        return new Tool
        {
            Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            Description = "Returns a browser approval URL for a pending plan, or applies it after approval. " +
                          "When this returns ApprovalRequired, you MUST call wait_for_plan_approval(planId=...) to poll for approval — do NOT wait for user confirmation. " +
                          "Repeat until Approved, then call this tool again to apply the plan.",
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

    private static Tool CreateWaitForPlanApprovalTool()
    {
        return new Tool
        {
            Name = McpGatewayConventions.ToolNames.WaitForPlanApproval,
            Description = "Waits briefly for an approval plan to become approved, applied, expired, or missing without applying the plan.",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    planId = new { type = "string", description = "PlanId returned by one of the request_* tools." },
                    timeoutSeconds = new
                    {
                        type = "integer",
                        description = "How long to wait before returning ApprovalRequired with timedOut=true.",
                        minimum = 1,
                        maximum = 300,
                        @default = 55
                    }
                },
                required = WaitForPlanApprovalRequiredArgs
            })
        };
    }

    private static Tool CreateProposePlanTool()
    {
        return new Tool
        {
            Name = McpGatewayConventions.ToolNames.ProposePlan,
            Description = "Creates an operator-approved remediation plan and sends an approval access code.",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    operationType = new
                    {
                        type = "string",
                        description = "Allowed values: restart_deployment, scale_deployment."
                    },
                    arguments = new
                    {
                        type = "object",
                        description = "Operation-specific arguments for the selected remediation operation."
                    }
                },
                required = ProposePlanRequiredArgs
            })
        };
    }

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

    private static IReadOnlyDictionary<string, object?> ConvertObjectArguments(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = JsonElementToObject(property.Value);
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

    internal static bool TryGetWaitTimeoutSeconds(
        IReadOnlyDictionary<string, object?> args,
        out int timeoutSeconds,
        out string timeoutError)
    {
        timeoutSeconds = WaitForPlanApprovalDefaultTimeoutSeconds;
        timeoutError = string.Empty;

        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.TimeoutSeconds, out var timeoutObj))
        {
            return true;
        }

        if (timeoutObj is int timeout)
        {
            timeoutSeconds = timeout;
        }
        else if (timeoutObj is double doubleTimeout &&
                 double.IsInteger(doubleTimeout) && // NOSONAR:S1244 — intended API, not an equality comparison
                 doubleTimeout >= int.MinValue &&
                 doubleTimeout <= int.MaxValue)
        {
            timeoutSeconds = (int)doubleTimeout;
        }
        else
        {
            timeoutError = "timeoutSeconds must be an integer between 1 and 300.";
            return false;
        }

        if (timeoutSeconds is < WaitForPlanApprovalMinimumTimeoutSeconds or > WaitForPlanApprovalMaximumTimeoutSeconds)
        {
            timeoutError = "timeoutSeconds must be an integer between 1 and 300.";
            return false;
        }

        return true;
    }

    private static bool IsTerminalWaitStatus(PlanStatus status) =>
        status is PlanStatus.NotFound or PlanStatus.Approved or PlanStatus.Applied or PlanStatus.Expired;

    private async Task WritePlanAuditAsync(PlanAudit audit, string context, CancellationToken ct)
    {
        try
        {
            await auditPublisher.PublishAsync(audit, ct).ConfigureAwait(false);
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

    private async Task<CallToolResult?> RequireAnyToolScopeAsync(string toolName)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return ErrorResult(
                $"Refused: '{toolName}' requires an authenticated session.");
        }

        return await RequireAnyScopeAsync(
            toolName,
            McpGatewayConventions.ToolScopeRequirements.MutationScope,
            McpGatewayConventions.ToolScopeRequirements.ReadOnlyScope).ConfigureAwait(false);
    }

    private async Task<CallToolResult?> RequireMutationScopeAsync(string toolName)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return ErrorResult(
                $"Refused: '{toolName}' requires an authenticated session with the '{McpGatewayConventions.ToolScopeRequirements.MutationScope}' scope.");
        }

        if (!GatewayAuthentication.HasRequiredScope(user, McpGatewayConventions.ToolScopeRequirements.MutationScope))
        {
            return await DenyAndAuditAsync(toolName, McpGatewayConventions.ToolScopeRequirements.MutationScope).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<CallToolResult?> RequireAnyScopeAsync(string toolName, params string[] requiredScopes)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return ErrorResult(
                $"Refused: '{toolName}' requires an authenticated session with one of these scopes: {string.Join(", ", requiredScopes)}.");
        }

        if (!requiredScopes.Any(scope => GatewayAuthentication.HasRequiredScope(user, scope)))
        {
            return await DenyAndAuditAsync(toolName, string.Join(" or ", requiredScopes)).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<CallToolResult?> DenyAndAuditAsync(string toolName, string requiredScope)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return ErrorResult(
                $"Refused: '{toolName}' requires an authenticated session with the '{requiredScope}' scope.");
        }

        var identity = GatewayAuditIdentityResolver.Resolve(user);

        logger.LogWarning(
            "Tool '{ToolName}' denied: caller lacks required scope '{RequiredScope}'.",
            toolName,
            requiredScope);

        var auditEvent = new GuardrailAuditEvent(
            toolName,
            McpGatewayConventions.GuardrailAudit.RequestDirection,
            McpGatewayConventions.GuardrailAudit.DenyAction,
            [McpGatewayConventions.GuardrailCategories.ScopeDenied],
            PlanId: null,
            identity.Subject,
            identity.AuthenticationType,
            identity.IdentityKind);

        await auditStore.WriteAsync(auditEvent, CancellationToken.None).ConfigureAwait(false);

        return ErrorResult(
            $"Refused: tool '{toolName}' requires the '{requiredScope}' scope.");
    }
}
