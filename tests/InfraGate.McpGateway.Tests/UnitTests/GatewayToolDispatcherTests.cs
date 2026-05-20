using System.Security.Claims;
using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
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
        Assert.Contains("destructive", Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        Assert.Empty(context.Downstream.Calls);
    }

    [Fact]
    public async Task CallToolAsync_ApprovedPlanBlockedByExecutor_DoesNotMarkApplied()
    {
        var executor = new FakeDomainPlanExecutor(
            DomainPlanExecutionResult.Success("unused", null),
            DomainPlanExecutionResult.Blocked("Plan cannot be executed: live Kubernetes state has drifted."));
        var context = CreateContext(executor);
        var envelope = await CreateGrantedPlanAsync(context.Store);

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
        Assert.False(File.Exists(context.Store.GetAppliedPath(envelope.Id)));
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
    public async Task CallToolAsync_ApprovedPlanPassingPreExecutionGate_ExecutesPlan()
    {
        var executor = new FakeDomainPlanExecutor(
            DomainPlanExecutionResult.Success("Applied successfully.", "mcp-nginx-demo"),
            DomainPlanExecutionResult.Success("Pre-execution checks passed.", null));
        var context = CreateContext(executor);
        var envelope = await CreateGrantedPlanAsync(context.Store);

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
        Assert.True(File.Exists(context.Store.GetAppliedPath(envelope.Id)));
    }

    [Fact]
    public async Task CallToolAsync_ApprovedPlanPassingPreExecutionGate_WritesGrantValidatedAudit()
    {
        var executor = new FakeDomainPlanExecutor(
            DomainPlanExecutionResult.Success("Applied successfully.", "mcp-nginx-demo"),
            DomainPlanExecutionResult.Success("Pre-execution checks passed.", null));
        var context = CreateContext(executor);
        var envelope = await CreateGrantedPlanAsync(context.Store);

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

        string audit = await File.ReadAllTextAsync(context.Store.AuditPath, CancellationToken.None);

        Assert.Contains($@"""eventName"": ""{ApprovalConventions.AuditEvents.PreExecutionGrantValidated}""", audit);
        Assert.Contains($@"""planId"": ""{envelope.Id}""", audit);
    }

    [Fact]
    public async Task CallToolAsync_ApprovedPlanExecutionThrows_WritesExecutionFailedAudit()
    {
        var context = CreateContext(new ThrowingDomainPlanExecutor());
        var envelope = await CreateGrantedPlanAsync(context.Store);

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
        string audit = await File.ReadAllTextAsync(context.Store.AuditPath);

        Assert.True(result.IsError);
        Assert.Contains("execution failed", Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        Assert.Contains($@"""eventName"": ""{ApprovalConventions.AuditEvents.ApplyFailed}""", audit);
        Assert.False(File.Exists(context.Store.GetAppliedPath(envelope.Id)));
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
        var auditJson = await File.ReadAllTextAsync(context.Store.AuditPath);
        Assert.Contains($@"""namespace"": ""{targetNamespace}""", auditJson);
    }

    private static TestContext CreateContext(
        IDomainPlanExecutor planExecutor,
        IDomainPlanBuilder? planBuilder = null,
        IGatewayApprovalService? approvals = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-dispatcher-tests", Guid.NewGuid().ToString("N"));
        var storeOptions = new ApprovalStoreOptions(root);
        var store = new ApprovalStore(storeOptions);
        var challenges = new ApprovalChallengeStore(storeOptions);
        var gatewayOptions = new McpGatewayOptions(
            new GatewayAuthOptions("https://issuer.example.com"),
            "downstream.csproj",
            Path.Combine(root, "guardrails"),
            Directory.GetCurrentDirectory(),
            root,
            "http://gateway.test",
            McpGatewayOptions.DefaultApprovalChallengeTtl);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(GatewayAuthConventions.Claims.Subject, Subject),
                    new Claim(GatewayAuthConventions.Claims.Scope, "mcp:tools")
                ], "test"))
            }
        };
        var downstream = new FakeDownstream();
        var audit = new InMemoryAuditStore();
        var guardedRunner = new GuardedToolRunner(
            downstream,
            audit,
            httpContextAccessor,
            NullLogger<GuardedToolRunner>.Instance);
        var gatewayApprovalService = approvals ?? new GatewayApprovalService(
            store,
            challenges,
            new KubernetesPlanReviewAdapter(),
            new KubernetesPlanReviewRenderer(),
            new SameSubjectAuthorizationCheck(),
            gatewayOptions,
            httpContextAccessor,
            NullNotificationDispatcher.Instance,
            NullLogger<GatewayApprovalService>.Instance);

        var domainAdapter = new FakeDomainAdapter(
            planBuilder ?? new FakeDomainPlanBuilder(PlanBuildResult.Failed("not implemented")),
            planExecutor);

        return new TestContext(
            new GatewayToolDispatcher(
                new DownstreamToolRegistry(downstream),
                guardedRunner,
                domainAdapter,
                gatewayApprovalService,
                store,
                new ApprovalPreExecutionGate(store, new ApprovalStoreAuditPublisher(store)),
                httpContextAccessor,
                new SubscriptionRegistry(),
                NullLogger<GatewayToolDispatcher>.Instance),
            store,
            downstream);
    }

    private static async Task<PlanEnvelope> CreateGrantedPlanAsync(ApprovalStore store)
    {
        var envelope = CreatePlanEnvelope("mcp-nginx-demo");

        await store.CreatePlanAsync(envelope, "mcp-nginx-demo", CancellationToken.None);
        await store.CreateGrantAsync(envelope, Subject, "challenge-1", CancellationToken.None);

        return envelope;
    }

    private static PlanEnvelope CreatePlanEnvelope(string namespaceName) =>
        KubernetesApprovalAdapter.ToEnvelope(
            KubernetesApprovalAdapter.CreateEnvelope(
                ApprovalStore.NewPlanId(),
                KubernetesAdapterConventions.PlanOperations.Apply,
                DateTimeOffset.UtcNow,
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
            CancellationToken ct) =>
            builder.BuildAsync(mutationToolName, arguments, requester, ct);

        public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(PlanEnvelope envelope, CancellationToken ct) =>
            executor.CheckPreExecutionAsync(envelope, ct);

        public Task<DomainPlanExecutionResult> ExecuteAsync(PlanEnvelope envelope, CancellationToken ct) =>
            executor.ExecuteAsync(envelope, ct);

        public IPlanReview? TryDecodeForReview(PlanEnvelope envelope, out string? error)
        {
            error = null;
            return null;
        }

        public string RenderReviewContent(IPlanReview planReview) => string.Empty;

        public string RenderApprovalRequiredMessage(IPlanReview planReview, string approvalUrl, DateTimeOffset expiresAtUtc) =>
            string.Empty;
    }

    private sealed class FakeDomainPlanBuilder(PlanBuildResult result) : IDomainPlanBuilder
    {
        public Task<PlanBuildResult> BuildAsync(
            string mutationToolName,
            IReadOnlyDictionary<string, object?> arguments,
            PlanRequester requester,
            CancellationToken ct) =>
            Task.FromResult(result);
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
        public Task WriteAsync(GuardrailAuditEvent auditEvent, CancellationToken cancellationToken) =>
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
        ApprovalStore Store,
        FakeDownstream Downstream);

    private sealed class NullNotificationDispatcher : IApprovalNotificationDispatcher
    {
        public static readonly NullNotificationDispatcher Instance = new();

        public Task NotifyPlanApprovedAsync(string planId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
