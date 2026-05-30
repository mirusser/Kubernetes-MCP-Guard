using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.AccessCodes;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ProposePlanHandlerTests
{
    [Theory]
    [InlineData(KubernetesAdapterConventions.MutationTools.RestartDeployment)]
    [InlineData(KubernetesAdapterConventions.MutationTools.ScaleDeployment)]
    [InlineData(KubernetesAdapterConventions.MutationTools.SetDeploymentImage)]
    public void IsSupportedOperation_KnownOperation_ReturnsTrue(string operationType)
    {
        Assert.True(ProposePlanHandler.IsSupportedOperation(operationType));
    }

    [Theory]
    [InlineData("delete_resource")]
    [InlineData("create_namespace")]
    [InlineData("")]
    public void IsSupportedOperation_UnknownOperation_ReturnsFalse(string operationType)
    {
        Assert.False(ProposePlanHandler.IsSupportedOperation(operationType));
    }

    [Fact]
    public async Task ProposeAsync_UnsupportedOperation_ReturnsErrorResult()
    {
        var handler = CreateHandler();

        var result = await handler.ProposeAsync(
            "delete_resource",
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.True(result.IsError);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("Refused", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProposeAsync_UnauthenticatedUser_ReturnsErrorResult()
    {
        var httpContext = new DefaultHttpContext();
        var handler = CreateHandler(httpContext);

        var result = await handler.ProposeAsync(
            KubernetesAdapterConventions.MutationTools.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ProposeAsync_BuildFailed_ReturnsErrorResult()
    {
        var context = CreateAuthenticatedContext("user-sub");
        var handler = CreateHandler(
            context,
            buildResult: PlanBuildResult.Failed("Namespace not allowed."));

        var result = await handler.ProposeAsync(
            KubernetesAdapterConventions.MutationTools.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.True(result.IsError);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("Namespace not allowed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposeAsync_GateRefused_ReturnsErrorResult()
    {
        var envelope = new PlanEnvelope();
        var context = CreateAuthenticatedContext("user-sub");
        var handler = CreateHandler(
            context,
            buildResult: PlanBuildResult.Success(envelope, "plan-1", "default"),
            gateResult: ApprovalGateResult.Refused("Policy check failed.", "policy-denied"));

        var result = await handler.ProposeAsync(
            KubernetesAdapterConventions.MutationTools.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.True(result.IsError);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("Policy check failed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposeAsync_ApprovalRequired_ReturnsPlanIdInResult()
    {
        var envelope = new PlanEnvelope();
        var context = CreateAuthenticatedContext("user-sub");
        var handler = CreateHandler(
            context,
            buildResult: PlanBuildResult.Success(envelope, "plan-xyz", "default"),
            gateResult: ApprovalGateResult.RequiresApproval(
                "Approval required.",
                challengeId: "challenge-abc",
                expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(15)));

        var result = await handler.ProposeAsync(
            KubernetesAdapterConventions.MutationTools.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("plan-xyz", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposeAsync_ApprovalRequired_AccessCodeSentTrue_WhenEmailConfigured()
    {
        var envelope = new PlanEnvelope();
        var context = CreateAuthenticatedContext("user-sub");
        var handler = CreateHandler(
            context,
            buildResult: PlanBuildResult.Success(envelope, "plan-xyz", "default"),
            gateResult: ApprovalGateResult.RequiresApproval(
                "Approval required.",
                challengeId: "challenge-abc",
                expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(15)),
            operatorEmail: "ops@example.com");

        var result = await handler.ProposeAsync(
            KubernetesAdapterConventions.MutationTools.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProposeAsync_ApprovalRequired_AccessCodeSentFalse_WhenNoOperatorEmail()
    {
        var envelope = new PlanEnvelope();
        var context = CreateAuthenticatedContext("user-sub");
        var handler = CreateHandler(
            context,
            buildResult: PlanBuildResult.Success(envelope, "plan-xyz", "default"),
            gateResult: ApprovalGateResult.RequiresApproval(
                "Approval required.",
                challengeId: "challenge-abc",
                expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(15)),
            operatorEmail: null);

        var result = await handler.ProposeAsync(
            KubernetesAdapterConventions.MutationTools.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("false", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProposeAsync_EmailSendFails_StillReturnsSuccessWithFalseAccessCodeSent()
    {
        var envelope = new PlanEnvelope();
        var context = CreateAuthenticatedContext("user-sub");
        var handler = CreateHandler(
            context,
            buildResult: PlanBuildResult.Success(envelope, "plan-xyz", "default"),
            gateResult: ApprovalGateResult.RequiresApproval(
                "Approval required.",
                challengeId: "challenge-abc",
                expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(15)),
            operatorEmail: "ops@example.com",
            emailThrows: true);

        var result = await handler.ProposeAsync(
            KubernetesAdapterConventions.MutationTools.RestartDeployment,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.True(result.IsError is not true);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Assert.Contains("false", text, StringComparison.OrdinalIgnoreCase);
    }

    private static DefaultHttpContext CreateAuthenticatedContext(string subject)
    {
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new System.Security.Claims.Claim("sub", subject),
        };
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(claims, "Bearer"));
        return context;
    }

    private static ProposePlanHandler CreateHandler(
        HttpContext? httpContext = null,
        PlanBuildResult? buildResult = null,
        ApprovalGateResult? gateResult = null,
        string? operatorEmail = "ops@example.com",
        bool emailThrows = false)
    {
        var accessor = new StubHttpContextAccessor(httpContext ?? new DefaultHttpContext());
        return new ProposePlanHandler(
            new ConfigurableDomainAdapter(buildResult ?? PlanBuildResult.Failed("not implemented")),
            new StubApprovalPlanWorkflow(),
            new ConfigurableGatewayApprovalService(gateResult ?? ApprovalGateResult.Refused("not implemented", "stub")),
            new StubApprovalAccessCodeStore(),
            new ConfigurableApprovalEmailSender(emailThrows),
            CreateOptions(operatorEmail),
            accessor,
            NullLogger<ProposePlanHandler>.Instance);
    }

    private static McpGatewayOptions CreateOptions(string? operatorEmail = "ops@example.com") =>
        new(
            new GatewayAuthOptions("https://auth.test"),
            DownstreamProject: "InfraGate.KubernetesAdapter",
            GuardAuditRoot: ".guard-audit",
            WorkingDirectory: ".",
            ApprovalRoot: ".mcp-approvals",
            ApprovalBaseUrl: "http://gateway.test",
            ApprovalChallengeTtl: TimeSpan.FromMinutes(15),
            OperatorGroup: "infra-operators",
            OperatorEmail: operatorEmail);

    private sealed class StubHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => context; set { } }
    }

    private sealed class StubDomainAdapter : IDomainAdapter
    {
        public string AdapterId => "stub";

        public Task<PlanBuildResult> BuildAsync(
            string mutationToolName,
            IReadOnlyDictionary<string, object?> arguments,
            PlanRequester requester,
            ApprovalPolicy approvalPolicy,
            CancellationToken ct) =>
            Task.FromResult(PlanBuildResult.Failed("not implemented"));

        public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(
            PlanEnvelope envelope,
            CancellationToken ct) =>
            Task.FromResult(DomainPlanExecutionResult.Blocked("not implemented"));

        public Task<DomainPlanExecutionResult> ExecuteAsync(
            PlanEnvelope envelope,
            CancellationToken ct) =>
            Task.FromResult(DomainPlanExecutionResult.Blocked("not implemented"));

        public IPlanReview? TryDecodeForReview(PlanEnvelope envelope, out string? error)
        {
            error = null;
            return null;
        }
    }

    private sealed class ConfigurableDomainAdapter(PlanBuildResult buildResult) : IDomainAdapter
    {
        public string AdapterId => "stub";

        public Task<PlanBuildResult> BuildAsync(
            string mutationToolName,
            IReadOnlyDictionary<string, object?> arguments,
            PlanRequester requester,
            ApprovalPolicy approvalPolicy,
            CancellationToken ct) =>
            Task.FromResult(buildResult);

        public Task<DomainPlanExecutionResult> CheckPreExecutionAsync(
            PlanEnvelope envelope,
            CancellationToken ct) =>
            Task.FromResult(DomainPlanExecutionResult.Blocked("not implemented"));

        public Task<DomainPlanExecutionResult> ExecuteAsync(
            PlanEnvelope envelope,
            CancellationToken ct) =>
            Task.FromResult(DomainPlanExecutionResult.Blocked("not implemented"));

        public IPlanReview? TryDecodeForReview(PlanEnvelope envelope, out string? error)
        {
            error = null;
            return null;
        }
    }

    private sealed class StubApprovalPlanWorkflow : IApprovalPlanWorkflow
    {
        public Task<ApprovalPlanResult> CreatePlanAsync(
            PlanEnvelope envelope,
            string targetNamespace,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalPlanResult(envelope, string.Empty, string.Empty));

        public Task<PendingPlanResult> GetPendingPlanAsync(
            string planId,
            CancellationToken cancellationToken) =>
            Task.FromResult(PendingPlanResult.Denied("not implemented"));

        public Task<GrantedPlanResult> GetGrantedPlanAsync(
            string planId,
            CancellationToken cancellationToken) =>
            Task.FromResult(GrantedPlanResult.MissingGrant("not implemented"));

        public Task<PlanStatusResult> GetPlanStatusAsync(
            string planId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PlanStatusResult(PlanStatus.NotFound));
    }

    private sealed class StubGatewayApprovalService : IGatewayApprovalService
    {
        public Task<ApprovalGateResult> EnsureApprovedOrCreateChallengeAsync(
            string planId, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalGateResult.Refused("not implemented", "stub"));

        public Task<ApprovalPageModel> GetApprovalPageAsync(
            string challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalPageModel(false, "not implemented", null, null));

        public Task<ApprovalDecisionResult> ApproveChallengeAsync(
            string challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalDecisionResult(false, "not implemented"));

        public Task<ApprovalDecisionResult> DenyChallengeAsync(
            string challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalDecisionResult(false, "not implemented"));

        public Task<ApprovalDecisionResult> CancelChallengeAsync(
            string challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalDecisionResult(false, "not implemented"));
    }

    private sealed class ConfigurableGatewayApprovalService(ApprovalGateResult gateResult) : IGatewayApprovalService
    {
        public Task<ApprovalGateResult> EnsureApprovedOrCreateChallengeAsync(
            string planId, CancellationToken cancellationToken) =>
            Task.FromResult(gateResult);

        public Task<ApprovalPageModel> GetApprovalPageAsync(
            string challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalPageModel(false, "not implemented", null, null));

        public Task<ApprovalDecisionResult> ApproveChallengeAsync(
            string challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalDecisionResult(false, "not implemented"));

        public Task<ApprovalDecisionResult> DenyChallengeAsync(
            string challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalDecisionResult(false, "not implemented"));

        public Task<ApprovalDecisionResult> CancelChallengeAsync(
            string challengeId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalDecisionResult(false, "not implemented"));
    }

    private sealed class StubApprovalAccessCodeStore : IApprovalAccessCodeStore
    {
        public Task<ApprovalAccessCode> GenerateAsync(
            string challengeId, TimeSpan ttl, CancellationToken ct) =>
            Task.FromResult(new ApprovalAccessCode(
                "TESTCODE",
                challengeId,
                DateTimeOffset.UtcNow.AddMinutes(15)));

        public Task<ApprovalAccessCodeConsumeResult> ConsumeAsync(
            string code, CancellationToken ct) =>
            Task.FromResult(ApprovalAccessCodeConsumeResult.Invalid());
    }

    private sealed class StubApprovalEmailSender : IApprovalEmailSender
    {
        public Task SendAsync(ApprovalEmailContent content, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class ConfigurableApprovalEmailSender(bool throws) : IApprovalEmailSender
    {
        public Task SendAsync(ApprovalEmailContent content, CancellationToken ct) =>
            throws
                ? Task.FromException(new InvalidOperationException("smtp down"))
                : Task.CompletedTask;
    }
}
