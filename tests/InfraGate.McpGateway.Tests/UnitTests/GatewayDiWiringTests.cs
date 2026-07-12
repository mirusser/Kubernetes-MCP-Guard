using InfraGate.ApprovalUi;
using InfraGate.Approvals;
using InfraGate.Approvals.AccessCodes;
using InfraGate.Approvals.Audit;
using InfraGate.Approvals.Challenge;
using InfraGate.Approvals.Execution;
using InfraGate.Approvals.Grant;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.PreExecution;
using InfraGate.KubernetesAdapter;
using InfraGate.KubernetesAdapter.Approval;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Email;
using InfraGate.McpGateway.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class GatewayDiWiringTests
{
    [Fact]
    public void Resolve_GatewayApprovalService_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateOptions());
        services.AddSingleton<TestApprovalWorkflow>();
        services.AddSingleton<IApprovalPlanWorkflow>(sp => sp.GetRequiredService<TestApprovalWorkflow>());
        services.AddSingleton<IApprovalChallengeWorkflow>(sp => sp.GetRequiredService<TestApprovalWorkflow>());
        services.AddSingleton<IApprovalAuditOutbox>(sp => sp.GetRequiredService<TestApprovalWorkflow>());
        services.AddSingleton<IPlanReviewAdapter, KubernetesPlanReviewAdapter>();
        services.AddSingleton<IAuthorizationCheck, SameSubjectAuthorizationCheck>();
        services.AddSingleton<IApprovalNotificationDispatcher, ApprovalNotificationDispatcher>();
        services.AddSingleton<ISubscriptionRegistry, SubscriptionRegistry>();
        services.AddSingleton<PlanStatusResourceHandler>();
        services.AddSingleton<IGatewayApprovalService, GatewayApprovalService>();
        services.AddHttpContextAccessor();
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IGatewayApprovalService>();

        Assert.NotNull(resolved);
    }

    [Fact]
    public void Resolve_GuardedToolRunner_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDownstreamMcpClient, NullDownstreamClient>();
        services.AddSingleton<IGuardrailAuditStore, NullAuditStore>();
        services.AddSingleton(CreateOptions());
        services.AddSingleton<SensitiveDataRedactor>(sp =>
            new SensitiveDataRedactor(
                McpGatewayConventions.SensitiveDataRedaction.Defaults,
                sp.GetRequiredService<ILogger<SensitiveDataRedactor>>()));
        services.AddHttpContextAccessor();
        services.AddLogging();
        services.AddSingleton<GuardedToolRunner>(sp =>
            new GuardedToolRunner(
                sp.GetRequiredService<IDownstreamMcpClient>(),
                sp.GetRequiredService<IGuardrailAuditStore>(),
                sp.GetRequiredService<IHttpContextAccessor>(),
                sp.GetRequiredService<SensitiveDataRedactor>(),
                sp.GetRequiredService<ILogger<GuardedToolRunner>>()));

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<GuardedToolRunner>();

        Assert.NotNull(resolved);
    }

    [Fact]
    public void Resolve_SensitiveDataRedactor_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateOptions());
        services.AddLogging();
        services.AddSingleton<SensitiveDataRedactor>(sp =>
            new SensitiveDataRedactor(
                McpGatewayConventions.SensitiveDataRedaction.Defaults,
                sp.GetRequiredService<ILogger<SensitiveDataRedactor>>()));

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<SensitiveDataRedactor>();

        Assert.NotNull(resolved);
    }

    [Fact]
    public void Resolve_SanitizingToolCaller_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDownstreamMcpClient, NullDownstreamClient>();
        services.AddSingleton<IGuardrailAuditStore, NullAuditStore>();
        services.AddSingleton(CreateOptions());
        services.AddSingleton<SensitiveDataRedactor>(sp =>
            new SensitiveDataRedactor(
                McpGatewayConventions.SensitiveDataRedaction.Defaults,
                sp.GetRequiredService<ILogger<SensitiveDataRedactor>>()));
        services.AddHttpContextAccessor();
        services.AddLogging();
        services.AddSingleton<IToolCaller>(sp =>
            new SanitizingToolCaller(
                sp.GetRequiredService<IDownstreamMcpClient>(),
                sp.GetRequiredService<IGuardrailAuditStore>(),
                sp.GetRequiredService<IHttpContextAccessor>(),
                sp.GetRequiredService<SensitiveDataRedactor>(),
                sp.GetRequiredService<ILogger<SanitizingToolCaller>>()));

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IToolCaller>();

        Assert.NotNull(resolved);
        Assert.IsType<SanitizingToolCaller>(resolved);
    }

    [Fact]
    public void Resolve_FullIntegrationTestGraph_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateOptions());
        services.AddSingleton<IDownstreamMcpClient, NullDownstreamClient>();
        services.AddSingleton<IGuardrailAuditStore, NullAuditStore>();
        services.AddSingleton<SensitiveDataRedactor>(sp =>
            new SensitiveDataRedactor(
                McpGatewayConventions.SensitiveDataRedaction.Defaults,
                sp.GetRequiredService<ILogger<SensitiveDataRedactor>>()));
        services.AddSingleton<GuardedToolRunner>(sp =>
            new GuardedToolRunner(
                sp.GetRequiredService<IDownstreamMcpClient>(),
                sp.GetRequiredService<IGuardrailAuditStore>(),
                sp.GetRequiredService<IHttpContextAccessor>(),
                sp.GetRequiredService<SensitiveDataRedactor>(),
                sp.GetRequiredService<ILogger<GuardedToolRunner>>()));
        services.AddSingleton<TestApprovalWorkflow>();
        services.AddSingleton<IApprovalPlanWorkflow>(sp => sp.GetRequiredService<TestApprovalWorkflow>());
        services.AddSingleton<IApprovalChallengeWorkflow>(sp => sp.GetRequiredService<TestApprovalWorkflow>());
        services.AddSingleton<IApprovalExecutionWorkflow>(sp => sp.GetRequiredService<TestApprovalWorkflow>());
        services.AddSingleton<IApprovalAuditOutbox>(sp => sp.GetRequiredService<TestApprovalWorkflow>());
        services.AddSingleton<IAuthorizationCheck, SameSubjectAuthorizationCheck>();
        services.AddSingleton<ISubscriptionRegistry, SubscriptionRegistry>();
        services.AddSingleton<IApprovalNotificationDispatcher, ApprovalNotificationDispatcher>();
        services.AddSingleton<PlanStatusResourceHandler>();
        services.AddSingleton<IGatewayApprovalService, GatewayApprovalService>();
        services.AddSingleton<IApprovalPreExecutionGate, ApprovalPreExecutionGate>();
        services.AddSingleton<IApprovalAccessCodeStore, InMemoryApprovalAccessCodeStore>();
        services.AddSingleton<IApprovalEmailSender, NullApprovalEmailSender>();
        services.AddSingleton<IProposePlanHandler, ProposePlanHandler>();
        services.AddSingleton<IToolCaller>(sp => (IToolCaller)sp.GetRequiredService<IDownstreamMcpClient>());
        services.AddSingleton<IApprovalPageRenderer>(sp =>
            new ApprovalPageRenderer(sp, sp.GetRequiredService<ILoggerFactory>()));
        services.AddKubernetesAdapter();
        services.AddSingleton<DownstreamToolRegistry>();
        services.AddSingleton<IGatewayToolDispatcher, GatewayToolDispatcher>();
        services.AddSingleton<IToolScopeGuard, ToolScopeGuard>();
        services.AddHttpContextAccessor();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "di-wiring-dp", Guid.NewGuid().ToString("N"))))
            .SetApplicationName(ApprovalConventions.Application.Name);
        services.AddLogging();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IGatewayApprovalService>());
        Assert.NotNull(provider.GetRequiredService<IApprovalPageRenderer>());
        Assert.NotNull(provider.GetRequiredService<GuardedToolRunner>());
        Assert.NotNull(provider.GetRequiredService<IApprovalPreExecutionGate>());
        Assert.NotNull(provider.GetRequiredService<PlanStatusResourceHandler>());
        Assert.NotNull(provider.GetRequiredService<IGatewayToolDispatcher>());
    }

    private static McpGatewayOptions CreateOptions()
    {
        var auth = new GatewayAuthOptions(
            OAuthAuthority: "https://issuer.example.com",
            OAuthResource: "http://127.0.0.1:3001/mcp",
            OAuthScope: "mcp:tools",
            OAuthRequireHttpsMetadata: false,
            OAuthMetadataAddress: null,
            ApprovalOAuthClientId: GatewayAuthConventions.DefaultApprovalOAuthClientId,
            ApprovalOAuthAuthorizationEndpoint: "https://issuer.example.com/protocol/openid-connect/auth",
            ApprovalOAuthTokenEndpoint: "https://issuer.example.com/protocol/openid-connect/token");

        return new McpGatewayOptions(
            auth,
            DownstreamProject: "unused",
            GuardAuditRoot: Path.Combine(Path.GetTempPath(), "di-wiring-guard"),
            WorkingDirectory: Directory.GetCurrentDirectory(),
            ApprovalRoot: Path.Combine(Path.GetTempPath(), "di-wiring-approval"),
            ApprovalBaseUrl: null,
            ApprovalChallengeTtl: McpGatewayOptions.DefaultApprovalChallengeTtl);
    }

    private sealed class NullDownstreamClient : IDownstreamMcpClient, IToolCaller
    {
        public Task<string> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult("{}");

        public Task<string> CallAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken ct) =>
            CallToolAsync(toolName, arguments, ct);

        public Task<IReadOnlyList<DownstreamTool>> ListToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownstreamTool>>([]);
    }

    private sealed class NullAuditStore : IGuardrailAuditStore
    {
        public Task WriteAsync(GuardrailAuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NullApprovalEmailSender : IApprovalEmailSender
    {
        public Task SendAsync(ApprovalEmailContent content, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
