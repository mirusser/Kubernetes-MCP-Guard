using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.PreExecution;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.AuditPayloads;
using InfraGate.McpGateway.Notifications;
using ModelContextProtocol.Protocol;
using System.Security.Claims;

namespace InfraGate.McpGateway;

internal sealed class GatewayToolDispatcher( // NOSONAR:S107 — DI constructor; all params are required services.
    DownstreamToolRegistry registry,
    GuardedToolRunner guardedRunner,
    IDomainAdapter domainAdapter,
    IGatewayApprovalService approvals,
    IApprovalPlanWorkflow approvalPlans,
    IApprovalExecutionWorkflow approvalExecution,
    IApprovalAuditOutbox auditOutbox,
    IApprovalPreExecutionGate preExecutionGate,
    IProposePlanHandler proposePlanHandler,
    IToolScopeGuard scopeGuard,
    IHttpContextAccessor httpContextAccessor,
    IReadOnlyList<GatewayToolDispatcher.ReadOnlySource> readOnlySources,
    ILogger<GatewayToolDispatcher> logger) : IGatewayToolDispatcher
{
    private static readonly TimeSpan WaitForPlanApprovalPollInterval = TimeSpan.FromMilliseconds(250);

    // Primary + optional secondary (e.g. kubernetes-mcp-server) read-only downstream sources,
    // composed once in the composition root (see ConfigurationExtensions.RegisterReadOnlySources)
    // rather than resolved here via IServiceProvider — this class takes plain constructor
    // injection only. Only the primary ever participates in destructive/request_* routing —
    // see IsDestructiveToolAsync and ListToolsAsync, which read `registry` directly, never this list.
    internal sealed record class ReadOnlySource(DownstreamToolRegistry Registry, GuardedToolRunner Runner);

    public async Task<ListToolsResult> ListToolsAsync(
        ListToolsRequestParams request,
        CancellationToken ct)
    {
        var tools = new List<Tool>();

        foreach (ReadOnlySource source in readOnlySources)
        {
            IReadOnlyList<DownstreamTool> readOnly = await source.Registry.GetReadOnlyAsync(ct).ConfigureAwait(false);
            foreach (DownstreamTool dt in readOnly)
            {
                tools.Add(ToolDefinitionFactory.CreateForwardedTool(dt));
            }
        }

        IReadOnlyList<DownstreamTool> destructive = await registry.GetDestructiveAsync(ct).ConfigureAwait(false);
        foreach (DownstreamTool dt in destructive)
        {
            string requestName = McpGatewayConventions.ToolNames.RequestToolPrefix + dt.Name;
            string requestDescription = $"Creates a pending approval plan for '{dt.Name}'. {dt.Description}";

            tools.Add(new Tool
            {
                Name = requestName,
                Description = requestDescription,
                InputSchema = dt.InputSchema
            });
        }

        tools.Add(ToolDefinitionFactory.CreateApplyApprovedPlanTool());
        tools.Add(ToolDefinitionFactory.CreateGetPlanStatusTool());
        tools.Add(ToolDefinitionFactory.CreateProposePlanTool());
        tools.Add(ToolDefinitionFactory.CreateWaitForPlanApprovalTool());

        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        if (user is not null)
        {
            tools = tools
                .Where(t => ToolScopeCatalog.IsVisibleTo(t.Name, t.Annotations?.ReadOnlyHint == true, user))
                .ToList();
        }

        return new ListToolsResult { Tools = tools };
    }

    public async Task<CallToolResult> CallToolAsync(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        try
        {
            return await CallToolAsyncCore(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unhandled exception dispatching tool '{ToolName}'", request.Name);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Handler error ({ex.GetType().Name}): {ex.Message}" }],
                IsError = true
            };
        }
    }

    private async Task<CallToolResult> CallToolAsyncCore(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        GuardrailContext.Reset();

        string toolName = request.Name;

        IReadOnlyList<string>? synthesizedScopes = ToolScopeCatalog.GetSynthesizedScopes(toolName);
        if (synthesizedScopes is not null)
        {
            CallToolResult? scopeResult = await scopeGuard.RequireAnyScopeAsync(toolName, synthesizedScopes.ToArray())
                .ConfigureAwait(false);
            if (scopeResult is not null)
            {
                return scopeResult;
            }

            if (toolName.Equals(McpGatewayConventions.ToolNames.ApplyApprovedPlan, StringComparison.Ordinal))
            {
                return await HandleApplyApprovedPlanAsync(request, ct).ConfigureAwait(false);
            }

            if (toolName.Equals(McpGatewayConventions.ToolNames.GetPlanStatus, StringComparison.Ordinal))
            {
                return await HandleGetPlanStatusAsync(request, ct).ConfigureAwait(false);
            }

            if (toolName.Equals(McpGatewayConventions.ToolNames.WaitForPlanApproval, StringComparison.Ordinal))
            {
                return await HandleWaitForPlanApprovalAsync(request, ct).ConfigureAwait(false);
            }

            if (toolName.Equals(McpGatewayConventions.ToolNames.ProposePlan, StringComparison.Ordinal))
            {
                return await HandleProposePlanAsync(request, ct).ConfigureAwait(false);
            }

            if (toolName.StartsWith(McpGatewayConventions.ToolNames.RequestToolPrefix, StringComparison.Ordinal))
            {
                return await HandleRequestMutationAsync(toolName, request, ct).ConfigureAwait(false);
            }
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
                McpGatewayMessages.ToolRouting.DestructiveToolRequiresRequest(toolName));
        }

        return ErrorResult(McpGatewayMessages.ToolRouting.UnknownTool(toolName));
    }

    private async Task<bool> IsReadOnlyToolAsync(string toolName, CancellationToken ct)
    {
        foreach (ReadOnlySource source in readOnlySources)
        {
            IReadOnlyList<DownstreamTool> readOnlyTools = await source.Registry.GetReadOnlyAsync(ct).ConfigureAwait(false);
            if (readOnlyTools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> IsDestructiveToolAsync(string toolName, CancellationToken ct)
    {
        IReadOnlyList<DownstreamTool> destructiveTools = await registry.GetDestructiveAsync(ct).ConfigureAwait(false);
        return destructiveTools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
    }

    private async Task<CallToolResult> HandleReadOnlyAsync(
        string toolName,
        CallToolRequestParams request,
        CancellationToken ct)
    {
        IReadOnlyList<string> scopes = ToolScopeCatalog.GetRequiredScopes(toolName, hasReadOnlyHint: true);
        CallToolResult? scopeResult = await scopeGuard.RequireAnyScopeAsync(toolName, scopes.ToArray()).ConfigureAwait(false);
        if (scopeResult is not null)
        {
            return scopeResult;
        }

        GuardedToolRunner? runner = null;
        foreach (ReadOnlySource source in readOnlySources)
        {
            IReadOnlyList<DownstreamTool> readOnlyTools = await source.Registry.GetReadOnlyAsync(ct).ConfigureAwait(false);
            if (readOnlyTools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal)))
            {
                runner = source.Runner;
                break;
            }
        }

        if (runner is null)
        {
            return ErrorResult(McpGatewayMessages.ToolRouting.UnknownTool(toolName));
        }

        IReadOnlyDictionary<string, object?> arguments = ToolArgumentConverter.ConvertArguments(request.Arguments);
        GuardedToolCallResult result = await runner.CallForModelVisibleResponseAsync(toolName, arguments, ct)
            .ConfigureAwait(false);
        string envelope = ModelVisibleToolResultEnvelope.Serialize(toolName, result, TimeProvider.System.GetUtcNow());

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = envelope }]
        };
    }

    private async Task<CallToolResult> HandleRequestMutationAsync(
        string toolName,
        CallToolRequestParams request,
        CancellationToken ct)
    {
        string mutationToolName = toolName[McpGatewayConventions.ToolNames.RequestToolPrefix.Length..];

        GatewayApprovalIdentity? identity = GatewayApprovalIdentityResolver.Resolve(httpContextAccessor.HttpContext?.User);
        if (identity is null)
        {
            return ErrorResult(McpGatewayMessages.Authorization.MutationRequiresAuth);
        }

        IReadOnlyDictionary<string, object?> args = ToolArgumentConverter.ConvertArguments(request.Arguments);
        bool requestHasFindings = await guardedRunner.AuditRequestAsync(toolName, args, ct).ConfigureAwait(false);

        PlanBuildResult planResult;
        try
        {
            planResult = await domainAdapter.BuildAsync(
                mutationToolName,
                args,
                new PlanRequester(identity.Subject, identity.AuthenticationType),
                ApprovalPolicy.SameSubject(),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Plan building threw an unhandled exception for tool '{ToolName}'", mutationToolName);
            return ErrorResult($"Plan building failed: {ex.GetType().Name}: {ex.Message}");
        }

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

        string message = McpGatewayMessages.ToolRouting.PlanCreated(planResult.PlanId);
        if (requestHasFindings || GuardrailContext.HasResponseFindings)
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
            await WriteAuditEntryAsync(audit, mutationToolName, ct).ConfigureAwait(false);
        }

        ResponseSanitizationResult sanitized = await guardedRunner.SanitizeAndAuditResponseAsync(toolName, args, planResult.Message, ct)
            .ConfigureAwait(false);
        string errorText = requestHasFindings || sanitized.HasFindings || GuardrailContext.HasResponseFindings
            ? GuardedToolRunner.FormatWarningResponse(sanitized.Text)
            : sanitized.Text;

        return ErrorResult(errorText);
    }

    private async Task<CallToolResult> HandleApplyApprovedPlanAsync(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        IReadOnlyDictionary<string, object?> args = ToolArgumentConverter.ConvertArguments(request.Arguments);
        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.PlanId, out object? planIdObj) ||
            planIdObj is not string planId ||
            string.IsNullOrWhiteSpace(planId))
        {
            return ErrorResult(McpGatewayMessages.ArgumentValidation.MissingPlanId);
        }

        ApprovalGateResult gate = await approvals.EnsureApprovedOrCreateChallengeAsync(planId, ct).ConfigureAwait(false);
        if (!gate.IsApproved)
        {
            return HandleUnapprovedGate(gate);
        }

        GrantedPlanResult granted = await approvalPlans.GetGrantedPlanAsync(planId, ct).ConfigureAwait(false);
        if (!granted.IsGranted || granted.Grant is null)
        {
            return ErrorResult(granted.Message);
        }

        BeginExecutionAttemptResult beginExecution = await approvalExecution.BeginExecutionAttemptAsync(planId, granted.Grant, ct)
            .ConfigureAwait(false);
        if (!beginExecution.IsStarted || beginExecution.Attempt is null)
        {
            return ErrorResult(beginExecution.Message);
        }

        PreExecutionGateResult preExecution = await preExecutionGate.EvaluateAsync(planId, domainAdapter, ct).ConfigureAwait(false);
        if (!preExecution.IsPassed || preExecution.Envelope is null || preExecution.Grant is null)
        {
            ApprovalAuditEntry audit = preExecution.Audit ?? new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.ApplyDenied,
                new ApplyDeniedPayload(planId, preExecution.Message),
                PlanId: planId);
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
        IReadOnlyDictionary<string, object?> args = ToolArgumentConverter.ConvertArguments(request.Arguments);
        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.PlanId, out object? planIdObj) ||
            planIdObj is not string planId ||
            string.IsNullOrWhiteSpace(planId))
        {
            return ErrorResult(McpGatewayMessages.ArgumentValidation.MissingPlanId);
        }

        PlanStatusResult result = await approvalPlans.GetPlanStatusAsync(planId, ct).ConfigureAwait(false);
        string json = PlanStatusResponse.Serialize(planId, result.Status);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }]
        };
    }

    private async Task<CallToolResult> HandleWaitForPlanApprovalAsync(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        IReadOnlyDictionary<string, object?> args = ToolArgumentConverter.ConvertArguments(request.Arguments);
        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.PlanId, out object? planIdObj) ||
            planIdObj is not string planId ||
            string.IsNullOrWhiteSpace(planId))
        {
            return ErrorResult(McpGatewayMessages.ArgumentValidation.MissingPlanId);
        }

        if (!ToolArgumentConverter.TryGetWaitTimeoutSeconds(args, out int timeoutSeconds, out string? timeoutError))
        {
            return ErrorResult(timeoutError);
        }

        DateTimeOffset deadline = TimeProvider.System.GetUtcNow().AddSeconds(timeoutSeconds);
        bool timedOut = false;
        PlanStatusResult result;
        do
        {
            result = await approvalPlans.GetPlanStatusAsync(planId, ct).ConfigureAwait(false);
            if (IsTerminalWaitStatus(result.Status))
            {
                break;
            }

            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            if (now >= deadline)
            {
                timedOut = true;
                break;
            }

            TimeSpan delay = deadline - now;
            if (delay > WaitForPlanApprovalPollInterval)
            {
                delay = WaitForPlanApprovalPollInterval;
            }

            await Task.Delay(delay, TimeProvider.System, ct).ConfigureAwait(false);
        } while (true);

        string json = PlanStatusResponse.Serialize(planId, result.Status, timedOut);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }]
        };
    }

    private async Task<CallToolResult> HandleProposePlanAsync(
        CallToolRequestParams request,
        CancellationToken ct)
    {
        IReadOnlyDictionary<string, object?> args = ToolArgumentConverter.ConvertArguments(request.Arguments);
        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.OperationType, out object? operationTypeObj) ||
            operationTypeObj is not string operationType ||
            string.IsNullOrWhiteSpace(operationType))
        {
            return ErrorResult(McpGatewayMessages.ArgumentValidation.MissingOperationType);
        }

        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.OperationArguments, out object? argumentsObj) ||
            argumentsObj is not JsonElement argumentsElement ||
            argumentsElement.ValueKind != JsonValueKind.Object)
        {
            return ErrorResult(McpGatewayMessages.ArgumentValidation.MissingArguments);
        }

        IReadOnlyDictionary<string, object?> operationArguments = ToolArgumentConverter.ConvertObjectArguments(argumentsElement);
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
            string message = McpGatewayMessages.Approval.PlanExecutionFailed(planId, ex.Message);
            var audit = new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.ApplyFailed,
                new ApplyFailedPayload(
                    planId,
                    envelope.Operation,
                    message),
                PlanId: planId);
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
            ApprovalAuditEntry audit = executeResult.Audit ?? new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.ApplyFailed,
                new ApplyFailedPayload(planId, envelope.Operation, executeResult.Message),
                PlanId: planId);
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
            new ApprovalAuditEntry(
                ApprovalConventions.AuditEvents.PlanApplied,
                new PlanAppliedPayload(envelope.Id, envelope.Operation, targetNamespace, grant.ReviewDigest.Value),
                PlanId: envelope.Id,
                GrantId: grant.Id),
            ct).ConfigureAwait(false);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = executeResult.Message }]
        };
    }

    private static bool IsTerminalWaitStatus(PlanStatus status) =>
        status is PlanStatus.NotFound or PlanStatus.Approved or PlanStatus.Applied or PlanStatus.Expired;

    private async Task WriteAuditEntryAsync(ApprovalAuditEntry entry, string context, CancellationToken ct)
    {
        try
        {
            await auditOutbox.AppendAsync(entry, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to write audit event {EventName} for {Context}.",
                entry.EventName,
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
