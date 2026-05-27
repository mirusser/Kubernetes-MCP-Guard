using System.Security.Claims;
using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Email;
using InfraGate.McpGateway.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayToolDispatcherTests
{
    private const string Subject = "requester";
    private const string RefusedReasonCode = "approval.refused.test";

    [Fact]
    public async Task ListToolsAsync_GetPlanStatus_ReturnsTool()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));

        var result = await context.Dispatcher.ListToolsAsync(new ListToolsRequestParams(), CancellationToken.None);

        Assert.Contains(result.Tools, tool => tool.Name == McpGatewayConventions.ToolNames.GetPlanStatus);
    }

    [Fact]
    public async Task ListToolsAsync_WaitForPlanApproval_ReturnsTool()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));

        var result = await context.Dispatcher.ListToolsAsync(new ListToolsRequestParams(), CancellationToken.None);

        Assert.Contains(result.Tools, tool => tool.Name == McpGatewayConventions.ToolNames.WaitForPlanApproval);
    }

    [Fact]
    public async Task ListToolsAsync_ProposePlan_ReturnsTool()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));

        var result = await context.Dispatcher.ListToolsAsync(new ListToolsRequestParams(), CancellationToken.None);

        Assert.Contains(result.Tools, tool => tool.Name == McpGatewayConventions.ToolNames.ProposePlan);
    }

    [Fact]
    public async Task CallToolAsync_GetPlanStatus_MissingPlanId_ReturnsError()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.GetPlanStatus
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(McpGatewayMessages.ArgumentValidation.MissingPlanId, Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
    }

    [Fact]
    public async Task CallToolAsync_WaitForPlanApproval_MissingPlanId_ReturnsError()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.WaitForPlanApproval
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(McpGatewayMessages.ArgumentValidation.MissingPlanId, Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
    }

    [Fact]
    public async Task CallToolAsync_GetPlanStatus_UnknownPlan_ReturnsNotFoundJson()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));
        string planId = ApprovalIds.NewPlanId();

        var result = await CallGetPlanStatusAsync(context, planId);

        AssertPlanStatusJson(result, planId, ApprovalConventions.PlanStatusValues.NotFound);
    }

    [Fact]
    public async Task CallToolAsync_WaitForPlanApproval_UnknownPlan_ReturnsNotFoundJson()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));
        string planId = ApprovalIds.NewPlanId();

        var result = await CallWaitForPlanApprovalAsync(context, planId);

        AssertPlanStatusJson(result, planId, ApprovalConventions.PlanStatusValues.NotFound, timedOut: false);
    }

    [Fact]
    public async Task CallToolAsync_WaitForPlanApproval_WithExecuteScope_ReturnsStatusJson()
    {
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            httpScope: McpGatewayConventions.ToolScopeRequirements.ExecuteScope);
        string planId = ApprovalIds.NewPlanId();

        var result = await CallWaitForPlanApprovalAsync(context, planId);

        AssertPlanStatusJson(result, planId, ApprovalConventions.PlanStatusValues.NotFound, timedOut: false);
    }

    [Fact]
    public async Task CallToolAsync_WaitForPlanApproval_PendingPlanTimesOut_ReturnsApprovalRequiredJson()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));
        var envelope = CreatePlanEnvelope("mcp-nginx-demo");
        await context.Workflow.CreatePlanAsync(envelope, "mcp-nginx-demo", CancellationToken.None);

        var result = await CallWaitForPlanApprovalAsync(context, envelope.Id, timeoutSeconds: 1);

        AssertPlanStatusJson(result, envelope.Id, ApprovalConventions.PlanStatusValues.ApprovalRequired, timedOut: true);
    }

    [Fact]
    public async Task CallToolAsync_WaitForPlanApproval_PendingPlanBecomesApproved_ReturnsApprovedJson()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));
        var envelope = CreatePlanEnvelope("mcp-nginx-demo");
        await context.Workflow.CreatePlanAsync(envelope, "mcp-nginx-demo", CancellationToken.None);

        var waitTask = CallWaitForPlanApprovalAsync(context, envelope.Id, timeoutSeconds: 1);
        await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System, CancellationToken.None);
        await context.Workflow.CreateGrantAsync(envelope, Subject, "challenge-1", CancellationToken.None);

        var result = await waitTask;

        AssertPlanStatusJson(result, envelope.Id, ApprovalConventions.PlanStatusValues.Approved, timedOut: false);
    }

    [Fact]
    public async Task CallToolAsync_WaitForPlanApproval_AppliedPlan_ReturnsAppliedJson()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));
        var envelope = await CreateGrantedPlanAsync(context.Workflow);
        var grant = await context.Workflow.GetGrantAsync(envelope.Id, CancellationToken.None);
        Assert.NotNull(grant);
        await context.Workflow.MarkAppliedAsync(envelope, "mcp-nginx-demo", grant, CancellationToken.None);

        var result = await CallWaitForPlanApprovalAsync(context, envelope.Id);

        AssertPlanStatusJson(result, envelope.Id, ApprovalConventions.PlanStatusValues.Applied, timedOut: false);
    }

    [Fact]
    public async Task CallToolAsync_WaitForPlanApproval_ExpiredPlan_ReturnsExpiredJson()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));
        var createdAtUtc = DateTimeOffset.UtcNow
            .Subtract(ApprovalConventions.PlanValidity.DefaultWindow)
            .Subtract(TimeSpan.FromMinutes(1));
        var envelope = await CreateGrantedPlanAsync(context.Workflow, createdAtUtc);

        var result = await CallWaitForPlanApprovalAsync(context, envelope.Id);

        AssertPlanStatusJson(result, envelope.Id, ApprovalConventions.PlanStatusValues.Expired, timedOut: false);
    }

    [Theory]
    [InlineData(1.0d, 1)]
    [InlineData(60.0d, 60)]
    [InlineData(300.0d, 300)]
    public void TryGetWaitTimeoutSeconds_IntegralDouble_AcceptsValue(double input, int expected)
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpGatewayConventions.ToolArguments.TimeoutSeconds] = input
        };

        bool ok = ToolArgumentConverter.TryGetWaitTimeoutSeconds(args, out int timeout, out string? error);

        Assert.True(ok);
        Assert.Equal(expected, timeout);
    }

    [Theory]
    [InlineData(1.5d)]
    [InlineData(0.5d)]
    public void TryGetWaitTimeoutSeconds_NonIntegralDouble_RejectsValue(double input)
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpGatewayConventions.ToolArguments.TimeoutSeconds] = input
        };

        bool ok = ToolArgumentConverter.TryGetWaitTimeoutSeconds(args, out _, out string? error);

        Assert.False(ok);
        Assert.Equal(McpGatewayMessages.ArgumentValidation.TimeoutMustBeInteger, error);
    }

    [Fact]
    public void TryGetWaitTimeoutSeconds_NegativeIntegralDouble_RejectsValue()
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpGatewayConventions.ToolArguments.TimeoutSeconds] = -1.0d
        };

        bool ok = ToolArgumentConverter.TryGetWaitTimeoutSeconds(args, out _, out string? error);

        Assert.False(ok);
        Assert.Equal(McpGatewayMessages.ArgumentValidation.TimeoutMustBeInteger, error);
    }

    [Fact]
    public async Task CallToolAsync_GetPlanStatus_ApprovalRequired_ReturnsStatusJson()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));
        var envelope = CreatePlanEnvelope("mcp-nginx-demo");
        await context.Workflow.CreatePlanAsync(envelope, "mcp-nginx-demo", CancellationToken.None);

        var result = await CallGetPlanStatusAsync(context, envelope.Id);

        AssertPlanStatusJson(result, envelope.Id, ApprovalConventions.PlanStatusValues.ApprovalRequired);
    }

    [Fact]
    public async Task CallToolAsync_GetPlanStatus_Approved_ReturnsStatusJson()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));
        var envelope = await CreateGrantedPlanAsync(context.Workflow);

        var result = await CallGetPlanStatusAsync(context, envelope.Id);

        AssertPlanStatusJson(result, envelope.Id, ApprovalConventions.PlanStatusValues.Approved);
    }

    [Fact]
    public async Task CallToolAsync_GetPlanStatus_Applied_ReturnsStatusJson()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));
        var envelope = await CreateGrantedPlanAsync(context.Workflow);
        var grant = await context.Workflow.GetGrantAsync(envelope.Id, CancellationToken.None);
        Assert.NotNull(grant);
        await context.Workflow.MarkAppliedAsync(envelope, "mcp-nginx-demo", grant, CancellationToken.None);

        var result = await CallGetPlanStatusAsync(context, envelope.Id);

        AssertPlanStatusJson(result, envelope.Id, ApprovalConventions.PlanStatusValues.Applied);
    }

    [Fact]
    public async Task CallToolAsync_RawDestructiveTool_ReturnsErrorWithoutCallingDownstream()
    {
        var context = CreateContext(new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)));

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "apply_manifest",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["namespace"] = JsonSerializer.SerializeToElement("mcp-nginx-demo"),
                    ["manifest"] = JsonSerializer.SerializeToElement("apiVersion: v1")
                }
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(McpGatewayMessages.ToolRouting.DestructiveToolRequiresRequest("apply_manifest"), Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        Assert.Empty(context.Downstream.Calls);
    }

    [Fact]
    public async Task CallToolAsync_ApprovedPlanBlockedByExecutor_DoesNotMarkApplied()
    {
        var executor = new FakeDomainPlanExecutor(
            DomainPlanExecutionResult.Success("unused", null),
            DomainPlanExecutionResult.Blocked("Plan cannot be executed: live Kubernetes state has drifted."));
        var context = CreateContext(executor);
        var envelope = await CreateGrantedPlanAsync(context.Workflow);

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
                Arguments = new Dictionary<string, JsonElement>
                {
                    [McpGatewayConventions.ToolArguments.PlanId] = JsonSerializer.SerializeToElement(envelope.Id)
                }
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("drifted", Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        Assert.False(context.Workflow.IsApplied(envelope.Id));
        Assert.Empty(executor.ExecuteCalls);
        Assert.Single(executor.PreExecutionCalls);
    }

    [Fact]
    public async Task CallToolAsync_RefusedApprovalGate_ReturnsErrorWithoutParsingMessageText()
    {
        const string refusalMessage = "Approval cannot continue.";
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            approvals: new FakeGatewayApprovalService(
                ApprovalGateResult.Refused(refusalMessage, RefusedReasonCode)));

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
                Arguments = new Dictionary<string, JsonElement>
                {
                    [McpGatewayConventions.ToolArguments.PlanId] = JsonSerializer.SerializeToElement("plan-1")
                }
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(refusalMessage, Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
    }

    [Fact]
    public async Task CallToolAsync_ApprovalRequired_DoesNotSubscribeCurrentSession()
    {
        const string planId = "plan-1";
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            approvals: new FakeGatewayApprovalService(
                ApprovalGateResult.RequiresApproval("Approval required.")));
        context.Subscriptions.RegisterSession("session-1", new FakeSessionNotifier("session-1"));
        context.HttpContext.Items[NotificationsConventions.McpSessionIdItemKey] = "session-1";

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
                Arguments = new Dictionary<string, JsonElement>
                {
                    [McpGatewayConventions.ToolArguments.PlanId] = JsonSerializer.SerializeToElement(planId)
                }
            },
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        Assert.Empty(context.Subscriptions.GetSessionsForPlan(planId));
    }

    [Fact]
    public async Task CallToolAsync_ApprovedPlanPassingPreExecutionGate_ExecutesPlan()
    {
        var executor = new FakeDomainPlanExecutor(
            DomainPlanExecutionResult.Success("Applied successfully.", "mcp-nginx-demo"),
            DomainPlanExecutionResult.Success("Pre-execution checks passed.", null));
        var context = CreateContext(executor);
        var envelope = await CreateGrantedPlanAsync(context.Workflow);

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
                Arguments = new Dictionary<string, JsonElement>
                {
                    [McpGatewayConventions.ToolArguments.PlanId] = JsonSerializer.SerializeToElement(envelope.Id)
                }
            },
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        Assert.Single(executor.PreExecutionCalls);
        Assert.Single(executor.ExecuteCalls);
        Assert.True(context.Workflow.IsApplied(envelope.Id));
    }

    [Fact]
    public async Task CallToolAsync_ApprovedPlanWithExecuteScope_ExecutesPlan()
    {
        var executor = new FakeDomainPlanExecutor(
            DomainPlanExecutionResult.Success("Applied successfully.", "mcp-nginx-demo"),
            DomainPlanExecutionResult.Success("Pre-execution checks passed.", null));
        var context = CreateContext(
            executor,
            httpScope: McpGatewayConventions.ToolScopeRequirements.ExecuteScope);
        var envelope = await CreateGrantedPlanAsync(context.Workflow);

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
                Arguments = new Dictionary<string, JsonElement>
                {
                    [McpGatewayConventions.ToolArguments.PlanId] = JsonSerializer.SerializeToElement(envelope.Id)
                }
            },
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        Assert.Single(executor.ExecuteCalls);
        Assert.True(context.Workflow.IsApplied(envelope.Id));
    }

    [Fact]
    public async Task CallToolAsync_ApprovedPlanPassingPreExecutionGate_WritesGrantValidatedAudit()
    {
        var executor = new FakeDomainPlanExecutor(
            DomainPlanExecutionResult.Success("Applied successfully.", "mcp-nginx-demo"),
            DomainPlanExecutionResult.Success("Pre-execution checks passed.", null));
        var context = CreateContext(executor);
        var envelope = await CreateGrantedPlanAsync(context.Workflow);

        await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
                Arguments = new Dictionary<string, JsonElement>
                {
                    [McpGatewayConventions.ToolArguments.PlanId] = JsonSerializer.SerializeToElement(envelope.Id)
                }
            },
            CancellationToken.None);

        string audit = context.Workflow.GetAuditEventsJson();

        Assert.Contains($@"""eventName"": ""{ApprovalConventions.AuditEvents.PreExecutionGrantValidated}""", audit);
        Assert.Contains($@"""planId"": ""{envelope.Id}""", audit);
    }

    [Fact]
    public async Task CallToolAsync_ApprovedPlanExecutionThrows_WritesExecutionFailedAudit()
    {
        var context = CreateContext(new ThrowingDomainPlanExecutor());
        var envelope = await CreateGrantedPlanAsync(context.Workflow);

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.ApplyApprovedPlan,
                Arguments = new Dictionary<string, JsonElement>
                {
                    [McpGatewayConventions.ToolArguments.PlanId] = JsonSerializer.SerializeToElement(envelope.Id)
                }
            },
            CancellationToken.None);
        string audit = context.Workflow.GetAuditEventsJson();

        Assert.True(result.IsError);
        Assert.Equal(McpGatewayMessages.Approval.PlanExecutionFailed(envelope.Id, "downstream mutation failed"), Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        Assert.Contains($@"""eventName"": ""{ApprovalConventions.AuditEvents.ApplyFailed}""", audit);
        Assert.False(context.Workflow.IsApplied(envelope.Id));
    }

    [Fact]
    public async Task CallToolAsync_RequestMutation_StoresPlanWithDomainTargetNamespace()
    {
        const string targetNamespace = "adapter-owned";
        var envelope = CreatePlanEnvelope(targetNamespace);
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            new FakeDomainPlanBuilder(PlanBuildResult.Success(envelope, envelope.Id, targetNamespace)));

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "request_apply_manifest",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["manifest"] = JsonSerializer.SerializeToElement("apiVersion: v1")
                }
            },
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        var auditJson = context.Workflow.GetAuditEventsJson();
        Assert.Contains($@"""namespace"": ""{targetNamespace}""", auditJson);
    }

    [Fact]
    public async Task CallToolAsync_ReadOnlyToolWithReadOnlyScope_Succeeds()
    {
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            httpScope: GatewayAuthConventions.DefaultReadOnlyOAuthScope);

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "get_allowed_namespaces"
            },
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        Assert.Contains(context.Downstream.Calls, call => call == "get_allowed_namespaces");
    }

    [Fact]
    public async Task CallToolAsync_ReadOnlyToolWithoutScope_WritesScopeDeniedAudit()
    {
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            httpScope: "");

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "get_allowed_namespaces"
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.DoesNotContain(context.Downstream.Calls, call => call == "get_allowed_namespaces");

        var auditEvent = Assert.Single(context.GuardrailAudit.Events);
        Assert.Equal("get_allowed_namespaces", auditEvent.ToolName);
        Assert.Equal(McpGatewayConventions.GuardrailAudit.DenyAction, auditEvent.Action);
    }

    [Fact]
    public async Task CallToolAsync_MutationToolWithReadOnlyScope_WritesScopeDeniedAudit()
    {
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            httpScope: GatewayAuthConventions.DefaultReadOnlyOAuthScope);

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "request_scale_deployment"
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(GatewayAuthConventions.DefaultOAuthScope, result.Content.OfType<TextContentBlock>().Single().Text);

        var auditEvent = Assert.Single(context.GuardrailAudit.Events);
        Assert.Equal("request_scale_deployment", auditEvent.ToolName);
        Assert.Equal(McpGatewayConventions.GuardrailAudit.DenyAction, auditEvent.Action);
        Assert.Equal("request", auditEvent.Direction);
        Assert.Equal(Subject, auditEvent.Subject);
    }

    [Fact]
    public async Task CallToolAsync_ReadOnlyToolWithExecuteScope_WritesScopeDeniedAudit()
    {
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            httpScope: McpGatewayConventions.ToolScopeRequirements.ExecuteScope);

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "get_allowed_namespaces"
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.DoesNotContain(context.Downstream.Calls, call => call == "get_allowed_namespaces");
        Assert.Single(context.GuardrailAudit.Events);
    }

    [Fact]
    public async Task CallToolAsync_RequestMutationWithExecuteScope_WritesScopeDeniedAudit()
    {
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            httpScope: McpGatewayConventions.ToolScopeRequirements.ExecuteScope);

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = "request_scale_deployment"
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Single(context.GuardrailAudit.Events);
    }

    [Fact]
    public async Task CallToolAsync_ProposePlanWithProposeScope_CreatesOperatorPlanAndSendsAccessCode()
    {
        var plan = CreatePlanEnvelope("mcp-nginx-demo") with
        {
            ApprovalPolicy = ApprovalPolicy.OperatorApproval("kubernetes-operators"),
            Requester = new PlanRequester("service:planner", "oauth-jwt")
        };
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            planBuilder: new CapturingDomainPlanBuilder(PlanBuildResult.Success(
                plan,
                plan.Id,
                "mcp-nginx-demo")),
            httpScope: McpGatewayConventions.ToolScopeRequirements.ProposeScope,
            subject: "service:planner",
            operatorEmail: "ops@example.com");

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.ProposePlan,
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["operationType"] = JsonSerializer.SerializeToElement("restart_deployment"),
                    ["arguments"] = JsonSerializer.SerializeToElement(new
                    {
                        name = "demo",
                        @namespace = "mcp-nginx-demo"
                    })
                }
            },
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        var pending = await context.Workflow.GetPendingPlanAsync(plan.Id, CancellationToken.None);
        Assert.True(pending.IsPending);
        Assert.Equal(ApprovalConventions.ApprovalPolicyTypes.OperatorApproval, pending.Envelope?.ApprovalPolicy.Type);
        Assert.Equal("kubernetes-operators", pending.Envelope?.ApprovalPolicy.Parameters?[ApprovalConventions.ApprovalPolicyParameters.OperatorGroup]);
        Assert.Single(context.EmailSender.Sent);
        Assert.Contains("ops@example.com", context.EmailSender.Sent[0].ToAddress);

        string text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        using var document = JsonDocument.Parse(text);
        Assert.Equal(plan.Id, document.RootElement.GetProperty("planId").GetString());
        Assert.True(document.RootElement.GetProperty("accessCodeSent").GetBoolean());
        Assert.Equal("/approvals/code", new Uri(document.RootElement.GetProperty("approvalUrl").GetString()!).AbsolutePath);
    }

    [Fact]
    public async Task CallToolAsync_ProposePlanWithExecuteScope_Rejects()
    {
        var context = CreateContext(
            new FakeDomainPlanExecutor(DomainPlanExecutionResult.Success("unused", null)),
            httpScope: McpGatewayConventions.ToolScopeRequirements.ExecuteScope);

        var result = await context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.ProposePlan,
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["operationType"] = JsonSerializer.SerializeToElement("restart_deployment"),
                    ["arguments"] = JsonSerializer.SerializeToElement(new { name = "demo" })
                }
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Single(context.GuardrailAudit.Events);
    }

    private static TestContext CreateContext(
        IDomainPlanExecutor planExecutor,
        IDomainPlanBuilder? planBuilder = null,
        IGatewayApprovalService? approvals = null,
        string? httpScope = null,
        string subject = Subject,
        string? operatorEmail = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-dispatcher-tests", Guid.NewGuid().ToString("N"));
        var workflow = new TestApprovalWorkflow();
        var gatewayOptions = new McpGatewayOptions(
            new GatewayAuthOptions("https://issuer.example.com"),
            "downstream.csproj",
            Path.Combine(root, "guardrails"),
            Directory.GetCurrentDirectory(),
            root,
            "http://gateway.test",
            McpGatewayOptions.DefaultApprovalChallengeTtl,
            OperatorEmail: operatorEmail);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(GatewayAuthConventions.Claims.Subject, subject),
                new Claim(GatewayAuthConventions.Claims.Scope, httpScope ?? "mcp:tools")
            ], "test"))
        };
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };
        var downstream = new FakeDownstream();
        var audit = new InMemoryAuditStore();
        var guardedRunner = new GuardedToolRunner(
            downstream,
            audit,
            httpContextAccessor,
            NullLogger<GuardedToolRunner>.Instance);
        var gatewayApprovalService = approvals ?? new GatewayApprovalService(
            workflow,
            workflow,
            workflow,
            new KubernetesPlanReviewAdapter(),
            new SameSubjectAuthorizationCheck(),
            gatewayOptions,
            httpContextAccessor,
            NullNotificationDispatcher.Instance,
            NullLogger<GatewayApprovalService>.Instance);

        var domainAdapter = new FakeDomainAdapter(
            planBuilder ?? new FakeDomainPlanBuilder(PlanBuildResult.Failed("not implemented")),
            planExecutor);
        var subscriptions = new SubscriptionRegistry();

        var guardrailAudit = new InMemoryAuditStore();
        var emailSender = new FakeApprovalEmailSender();
        var proposePlanHandler = new ProposePlanHandler(
            domainAdapter,
            workflow,
            gatewayApprovalService,
            new InMemoryApprovalAccessCodeStore(),
            emailSender,
            gatewayOptions,
            httpContextAccessor,
            NullLogger<ProposePlanHandler>.Instance);

        return new TestContext(
            new GatewayToolDispatcher(
                new DownstreamToolRegistry(downstream),
                guardedRunner,
                domainAdapter,
                gatewayApprovalService,
                workflow,
                workflow,
                workflow,
                new ApprovalPreExecutionGate(workflow, workflow),
                proposePlanHandler,
                subscriptions,
                new ToolScopeGuard(httpContextAccessor, guardrailAudit, NullLogger<ToolScopeGuard>.Instance),
                httpContextAccessor,
                NullLogger<GatewayToolDispatcher>.Instance),
            workflow,
            downstream,
            subscriptions,
            httpContext,
            guardrailAudit,
            emailSender);
    }

    private static async Task<PlanEnvelope> CreateGrantedPlanAsync(
        TestApprovalWorkflow workflow,
        DateTimeOffset? createdAtUtc = null)
    {
        var envelope = CreatePlanEnvelope("mcp-nginx-demo", createdAtUtc);

        await workflow.CreatePlanAsync(envelope, "mcp-nginx-demo", CancellationToken.None);
        await workflow.CreateGrantAsync(envelope, Subject, "challenge-1", CancellationToken.None);

        return envelope;
    }

    private static PlanEnvelope CreatePlanEnvelope(string namespaceName, DateTimeOffset? createdAtUtc = null) =>
        KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                ApprovalIds.NewPlanId(),
                KubernetesAdapterConventions.PlanOperations.Apply,
                createdAtUtc ?? DateTimeOffset.UtcNow,
                new PlanRequester(Subject, "test"),
                new KubernetesPlanPayload(
                    namespaceName,
                    "Apply deployment.",
                    new Dictionary<string, string>
                    {
                        [KubernetesAdapterConventions.PlanParameters.ObjectCount] = "1"
                    },
                    [new KubernetesObjectRef("apps/v1", "Deployment", namespaceName, "demo")])
                {
                    Manifest = "apiVersion: apps/v1",
                    DryRun = new KubernetesPlanDryRun(
                        "succeeded",
                        DateTimeOffset.UtcNow,
                        [new KubernetesPlanDryRunObject($"apps/v1 Deployment {namespaceName}/demo", "{}")],
                        [],
                        "Server-side dry-run succeeded."),
                    Diffs =
                    [
                        new KubernetesPlanDiff(
                            new KubernetesObjectRef("apps/v1", "Deployment", namespaceName, "demo"),
                            ApprovalConventions.DiffChangeTypes.Update,
                            "Deployment will be updated.",
                            "@@ -1 +1 @@",
                            "{}",
                            "{}",
                            [],
                            [],
                            [])
                    ]
                }));

    private static Task<CallToolResult> CallGetPlanStatusAsync(TestContext context, string planId) =>
        context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.GetPlanStatus,
                Arguments = new Dictionary<string, JsonElement>
                {
                    [McpGatewayConventions.ToolArguments.PlanId] = JsonSerializer.SerializeToElement(planId)
                }
            },
            CancellationToken.None);

    private static Task<CallToolResult> CallWaitForPlanApprovalAsync(
        TestContext context,
        string planId,
        int? timeoutSeconds = null)
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            [McpGatewayConventions.ToolArguments.PlanId] = JsonSerializer.SerializeToElement(planId)
        };

        if (timeoutSeconds is { } value)
        {
            arguments[McpGatewayConventions.ToolArguments.TimeoutSeconds] = JsonSerializer.SerializeToElement(value);
        }

        return context.Dispatcher.CallToolAsync(
            new CallToolRequestParams
            {
                Name = McpGatewayConventions.ToolNames.WaitForPlanApproval,
                Arguments = arguments
            },
            CancellationToken.None);
    }

    private static void AssertPlanStatusJson(
        CallToolResult result,
        string planId,
        string expectedStatus,
        bool? timedOut = null)
    {
        Assert.True(result.IsError is not true);
        string text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        using var document = JsonDocument.Parse(text);

        Assert.Equal(
            planId,
            document.RootElement.GetProperty(McpGatewayConventions.ToolArguments.PlanId).GetString());
        Assert.Equal(
            expectedStatus,
            document.RootElement.GetProperty(McpGatewayConventions.ToolResponseFields.Status).GetString());
        if (timedOut is { } expectedTimedOut)
        {
            Assert.Equal(
                expectedTimedOut,
                document.RootElement.GetProperty(McpGatewayConventions.ToolResponseFields.TimedOut).GetBoolean());
        }
    }

    private sealed class FakeDownstream : IDownstreamMcpClient
    {
        private static readonly JsonElement DefaultSchema = JsonSerializer.SerializeToElement(new { type = "object" });

        public List<string> Calls { get; } = [];

        public Task<string> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add(toolName);
            return Task.FromResult("downstream result");
        }

        public Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownstreamTool>>(
            [
                new DownstreamTool("get_allowed_namespaces", "Returns allowed namespaces.", true, false, DefaultSchema),
                new DownstreamTool("apply_manifest", "Applies manifests.", false, true, DefaultSchema)
            ]);
    }

    private sealed class FakeDomainAdapter(
        IDomainPlanBuilder builder,
        IDomainPlanExecutor executor) : IDomainAdapter
    {
        public string AdapterId => "fake";

        public Task<PlanBuildResult> BuildAsync(
            string mutationToolName,
            IReadOnlyDictionary<string, object?> arguments,
            PlanRequester requester,
            ApprovalPolicy approvalPolicy,
            CancellationToken ct) =>
            builder.BuildAsync(mutationToolName, arguments, requester, approvalPolicy, ct);

        public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct) =>
            executor.CheckPreExecutionAsync(envelope, ct);

        public Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct) =>
            executor.ExecuteAsync(envelope, ct);

        public IPlanReview? TryDecodeForReview(PlanEnvelope envelope, out string? error)
        {
            error = null;
            return null;
        }
    }

    private sealed class FakeDomainPlanBuilder(PlanBuildResult result) : IDomainPlanBuilder
    {
        public Task<PlanBuildResult> BuildAsync(
            string mutationToolName,
            IReadOnlyDictionary<string, object?> arguments,
            PlanRequester requester,
            ApprovalPolicy approvalPolicy,
            CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class CapturingDomainPlanBuilder(PlanBuildResult result) : IDomainPlanBuilder
    {
        public List<(string OperationType, PlanRequester Requester, ApprovalPolicy ApprovalPolicy)> Calls { get; } = [];

        public Task<PlanBuildResult> BuildAsync(
            string mutationToolName,
            IReadOnlyDictionary<string, object?> arguments,
            PlanRequester requester,
            ApprovalPolicy approvalPolicy,
            CancellationToken ct)
        {
            Calls.Add((mutationToolName, requester, approvalPolicy));
            var envelope = result.Envelope is null
                ? null
                : result.Envelope with
                {
                    Requester = requester,
                    ApprovalPolicy = approvalPolicy
                };

            return Task.FromResult(envelope is null
                ? result
                : PlanBuildResult.Success(envelope, envelope.Id, result.TargetNamespace));
        }
    }

    private sealed class FakeDomainPlanExecutor(
        DomainPlanExecutionResult executeResult,
        DomainPlanExecutionResult? preExecutionResult = null) : IDomainPlanExecutor
    {
        public List<string> ExecuteCalls { get; } = [];

        public List<string> PreExecutionCalls { get; } = [];

        public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct)
        {
            PreExecutionCalls.Add(envelope.Id);
            return Task.FromResult(preExecutionResult ?? DomainPlanExecutionResult.Success("Pre-execution checks passed.", null));
        }

        public Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct)
        {
            ExecuteCalls.Add(envelope.Id);
            return Task.FromResult(executeResult);
        }
    }

    private sealed class ThrowingDomainPlanExecutor : IDomainPlanExecutor
    {
        public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct) =>
            Task.FromResult(DomainPlanExecutionResult.Success("Pre-execution checks passed.", null));

        public Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct) =>
            throw new InvalidOperationException("downstream mutation failed");
    }

    private sealed class InMemoryAuditStore : IGuardrailAuditStore
    {
        public List<GuardrailAuditEvent> Events { get; } = [];

        public Task WriteAsync(GuardrailAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApprovalEmailSender : IApprovalEmailSender
    {
        public List<ApprovalEmailContent> Sent { get; } = [];

        public Task SendAsync(ApprovalEmailContent content, CancellationToken cancellationToken)
        {
            Sent.Add(content);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionNotifier(string sessionId) : ISessionNotifier
    {
        public string? SessionId => sessionId;

        public Task SendNotificationAsync<TParams>(string method, TParams @params, CancellationToken ct)
            where TParams : notnull =>
            Task.CompletedTask;
    }

    private sealed class FakeGatewayApprovalService(ApprovalGateResult gateResult) : IGatewayApprovalService
    {
        public Task<ApprovalGateResult> EnsureApprovedOrCreateChallengeAsync(
            string planId,
            CancellationToken cancellationToken) =>
            Task.FromResult(gateResult);

        public Task<ApprovalPageModel> GetApprovalPageAsync(
            string challengeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApprovalDecisionResult> ApproveChallengeAsync(
            string challengeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApprovalDecisionResult> DenyChallengeAsync(
            string challengeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApprovalDecisionResult> CancelChallengeAsync(
            string challengeId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed record class TestContext(
        IGatewayToolDispatcher Dispatcher,
        TestApprovalWorkflow Workflow,
        FakeDownstream Downstream,
        SubscriptionRegistry Subscriptions,
        DefaultHttpContext HttpContext,
        InMemoryAuditStore GuardrailAudit,
        FakeApprovalEmailSender EmailSender);

    private sealed class NullNotificationDispatcher : IApprovalNotificationDispatcher
    {
        public static readonly NullNotificationDispatcher Instance = new();

        public Task NotifyPlanApprovedAsync(string planId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
