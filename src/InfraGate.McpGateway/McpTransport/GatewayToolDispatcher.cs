using System.Diagnostics.Metrics;
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
    DownstreamToolCatalog catalog,
    ILogger<GatewayToolDispatcher> logger) : IGatewayToolDispatcher
{
    private static readonly TimeSpan WaitForPlanApprovalPollInterval = TimeSpan.FromMilliseconds(250);

    private static readonly Meter Meter = new(
        McpGatewayConventions.Telemetry.MeterName,
        McpGatewayConventions.Telemetry.MeterVersion);

    private static readonly Counter<long> CatalogPublishCounter =
        Meter.CreateCounter<long>(McpGatewayConventions.Telemetry.DownstreamCatalogPublishCounterName);

    private readonly SemaphoreSlim catalogInitLock = new(1, 1);
    private volatile bool catalogPopulated;

    // Primary + optional secondary (e.g. kubernetes-mcp-server) read-only downstream sources,
    // composed once in the composition root (see ConfigurationExtensions.RegisterReadOnlySources)
    // rather than resolved here via IServiceProvider — this class takes plain constructor
    // injection only. Only the primary ever participates in destructive/request_* routing —
    // see IsDestructiveToolAsync and ListToolsAsync, which read `registry` directly, never this list.
    // Read-only routing (ListToolsAsync/DispatchDownstreamToolAsync) goes through `catalog`,
    // populated once from these sources by EnsureCatalogPopulatedAsync — see Task 9 of
    // docs/plans (one catalog with immutable source ownership; calls route through the
    // published catalog entry, not a per-source name lookup).
    internal sealed record class ReadOnlySource(
        string SourceId,
        DownstreamToolRegistry Registry,
        GuardedToolRunner Runner,
        KubernetesMcpServerRequestPolicy? RequestPolicy = null,
        KubernetesMcpServerResponsePolicy? ResponsePolicy = null,
        IReadOnlySet<string>? ExpectedTools = null,
        KubernetesMcpServerCapabilityManifest? CapabilityManifest = null,
        KubernetesMcpServerProcessRole? CapabilityRole = null);

    private async Task EnsureCatalogPopulatedAsync(CancellationToken ct)
    {
        if (catalogPopulated)
        {
            return;
        }

        await catalogInitLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (catalogPopulated)
            {
                return;
            }

            foreach (ReadOnlySource source in readOnlySources)
            {
                bool isMandatory = string.Equals(
                    source.SourceId, McpGatewayConventions.DownstreamSources.Primary, StringComparison.Ordinal);

                await PublishSourceSnapshotAsync(source, isMandatory, ct).ConfigureAwait(false);
            }

            catalogPopulated = true;
        }
        finally
        {
            catalogInitLock.Release();
        }
    }

    /// <summary>
    /// Re-lists and re-validates a single optional secondary source's tools and atomically swaps
    /// its catalog entries — see <see cref="IGatewayToolDispatcher.RegenerateSourceAsync"/>. Task
    /// 12's process supervisor calls this once a replacement process's session is ready; here it
    /// is exercised directly to prove the swap is atomic and failure-isolated (Task 11).
    /// </summary>
    public async Task RegenerateSourceAsync(string sourceId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        ReadOnlySource? source = FindSourceById(sourceId);
        bool isMandatory = string.Equals(
            sourceId, McpGatewayConventions.DownstreamSources.Primary, StringComparison.Ordinal);
        if (source is null || isMandatory)
        {
            return;
        }

        await catalogInitLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // The registry caches its last successful ListToolsAsync result forever; without
            // invalidating it first, PublishSourceSnapshotAsync would just re-publish the same
            // stale tool list instead of observing the replacement process's new one.
            await source.Registry.InvalidateAsync(ct).ConfigureAwait(false);
            await PublishSourceSnapshotAsync(source, isMandatory: false, ct).ConfigureAwait(false);
        }
        finally
        {
            catalogInitLock.Release();
        }
    }

    private async Task PublishSourceSnapshotAsync(ReadOnlySource source, bool isMandatory, CancellationToken ct)
    {
        IReadOnlyList<DownstreamTool> tools;
        if (isMandatory)
        {
            // Primary publication is mandatory: a failure here means the Gateway cannot
            // route anything, so it propagates rather than being silently swallowed.
            tools = await source.Registry.GetReadOnlyAsync(ct).ConfigureAwait(false);
        }
        else
        {
            try
            {
                tools = await source.Registry.GetReadOnlyAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Optional secondary source (timeout, unreachable process, transport
                // fault, etc.): omit it for this cycle and keep primary tools flowing.
                // The stable reason is logged separately from the exception detail so it
                // stays safe to surface via health/readiness reporting later (Task 13).
                logger.LogWarning(
                    ex,
                    "Optional downstream source '{SourceId}' failed to list tools; omitting its snapshot.",
                    source.SourceId);
                catalog.RecordSourceDegraded(
                    source.SourceId, McpGatewayMessages.ToolCatalog.SourceUnavailable);
                RecordCatalogPublishOutcome(source.SourceId, McpGatewayConventions.Telemetry.Outcomes.CatalogRejected);
                return;
            }
        }

        // No expectedTools admission gate here: a source may legitimately advertise more
        // read-only tools than the Gateway chooses to expose (e.g. the real
        // kubernetes-mcp-server binary). Visibility is filtered per-entry below via
        // RequestPolicy.IsToolAllowed, and dispatch of an unlisted-but-known tool name is
        // still denied (with an audit trail) by RequestPolicy.TryValidate in
        // HandleReadOnlyAsync — see KubernetesMcpServerRequestPolicy.
        ToolCatalogSnapshot snapshot = source.CapabilityManifest is null
            ? await catalog.PublishSnapshotAsync(
                source.SourceId,
                tools,
                expectedTools: null,
                expectedToolSchemas: null,
                source.RequestPolicy,
                source.ResponsePolicy,
                ct).ConfigureAwait(false)
            : await catalog.PublishCapabilitySnapshotAsync(
                source.SourceId,
                tools,
                source.ExpectedTools ?? throw new InvalidOperationException(
                    "Capability admission requires an exact expected tool set."),
                source.RequestPolicy,
                source.ResponsePolicy,
                source.CapabilityManifest,
                source.CapabilityRole ?? throw new InvalidOperationException(
                    "A capability manifest requires an explicit process role."),
                ct).ConfigureAwait(false);

        if (!snapshot.IsValid)
        {
            logger.LogWarning(
                "Tool catalog snapshot from source '{SourceId}' was rejected: {Reason}",
                source.SourceId,
                snapshot.DegradedReason);

            if (!isMandatory)
            {
                catalog.RecordSourceDegraded(source.SourceId, snapshot.DegradedReason ?? McpGatewayMessages.ToolCatalog.SourceUnavailable);
            }

            RecordCatalogPublishOutcome(source.SourceId, McpGatewayConventions.Telemetry.Outcomes.CatalogRejected);
        }
        else
        {
            if (!isMandatory)
            {
                catalog.RecordSourceHealthy(source.SourceId);
            }

            RecordCatalogPublishOutcome(source.SourceId, McpGatewayConventions.Telemetry.Outcomes.CatalogPublished);
        }
    }

    private static void RecordCatalogPublishOutcome(string sourceId, string outcome)
    {
        CatalogPublishCounter.Add(1,
            new KeyValuePair<string, object?>(McpGatewayConventions.Telemetry.Tags.Source, sourceId),
            new KeyValuePair<string, object?>(McpGatewayConventions.Telemetry.Tags.Outcome, outcome));
    }

    private ReadOnlySource? FindSourceById(string sourceId)
    {
        foreach (ReadOnlySource source in readOnlySources)
        {
            if (string.Equals(source.SourceId, sourceId, StringComparison.Ordinal))
            {
                return source;
            }
        }

        return null;
    }

    public async Task<ListToolsResult> ListToolsAsync(
        ListToolsRequestParams request,
        CancellationToken ct)
    {
        await EnsureCatalogPopulatedAsync(ct).ConfigureAwait(false);

        var tools = new List<Tool>();
        foreach (ToolCatalogEntry entry in catalog.GetAllEntries())
        {
            if (entry.RequestPolicy is not null && !entry.RequestPolicy.IsToolAllowed(entry.ToolName))
            {
                continue;
            }

            tools.Add(ToolDefinitionFactory.CreateForwardedTool(entry.Tool));
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
        await EnsureCatalogPopulatedAsync(ct).ConfigureAwait(false);

        ToolCatalogEntry? entry = catalog.GetCatalogEntry(toolName);
        if (entry is not null)
        {
            ReadOnlySource? source = FindSourceById(entry.SourceId);
            if (source is not null)
            {
                return await HandleReadOnlyAsync(entry, source.Runner, toolName, request, ct).ConfigureAwait(false);
            }
        }

        if (await IsDestructiveToolAsync(toolName, ct).ConfigureAwait(false))
        {
            return ErrorResult(
                McpGatewayMessages.ToolRouting.DestructiveToolRequiresRequest(toolName));
        }

        return ErrorResult(McpGatewayMessages.ToolRouting.UnknownTool(toolName));
    }

    private async Task<bool> IsDestructiveToolAsync(string toolName, CancellationToken ct)
    {
        IReadOnlyList<DownstreamTool> destructiveTools = await registry.GetDestructiveAsync(ct).ConfigureAwait(false);
        return destructiveTools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
    }

    private async Task<CallToolResult> HandleReadOnlyAsync(
        ToolCatalogEntry entry,
        GuardedToolRunner runner,
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

        IReadOnlyDictionary<string, object?> arguments = ToolArgumentConverter.ConvertArguments(request.Arguments);
        if (entry.RequestPolicy is not null
            && !entry.RequestPolicy.TryValidate(toolName, arguments, out string policyError))
        {
            await runner.AuditPolicyDenialAsync(
                toolName,
                arguments,
                McpGatewayConventions.GuardrailAudit.RequestDirection,
                McpGatewayConventions.GuardrailCategories.KubernetesRequestPolicy,
                metadata: null,
                ct).ConfigureAwait(false);
            return ErrorResult(policyError);
        }

        TypedGuardedToolCallResult result = await runner.CallForTypedResponseAsync(toolName, arguments, ct)
            .ConfigureAwait(false);

        (IReadOnlyList<object> envelopedContent, bool isError, System.Text.Json.Nodes.JsonObject? meta) =
            ModelVisibleToolResultEnvelope.CreateTypedEnvelope(toolName, result, TimeProvider.System.GetUtcNow());

        // Apply response policy if configured (only checks the envelope metadata, not all blocks)
        if (entry.ResponsePolicy is not null && envelopedContent.Count > 0 && envelopedContent[0] is TextContentBlock firstBlock)
        {
            KubernetesMcpServerResponsePolicyResult policyResult = entry.ResponsePolicy.Apply(toolName, firstBlock.Text);
            if (!policyResult.IsAllowed)
            {
                var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [McpGatewayConventions.GuardrailAudit.EntryFields.ActualBytes] = policyResult.Utf8ByteCount,
                    [McpGatewayConventions.GuardrailAudit.EntryFields.MaximumBytes] =
                        KubernetesMcpServerResponsePolicy.MaximumResponseBytes
                };
                await runner.AuditPolicyDenialAsync(
                    toolName,
                    arguments,
                    McpGatewayConventions.GuardrailAudit.ResponseDirection,
                    McpGatewayConventions.GuardrailCategories.ResponseSize,
                    metadata,
                    ct).ConfigureAwait(false);
                return ErrorResult(policyResult.Error);
            }
        }

        return new CallToolResult
        {
            Content = envelopedContent.Cast<ContentBlock>().ToList(),
            IsError = isError,
            Meta = meta
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
