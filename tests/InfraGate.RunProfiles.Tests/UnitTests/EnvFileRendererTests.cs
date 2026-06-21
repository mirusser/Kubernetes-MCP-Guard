using InfraGate.RunProfiles;

namespace InfraGate.RunProfiles.Tests.UnitTests;

public sealed class EnvFileRendererTests
{
    [Fact]
    public void Render_EmptyConfigFileName_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            EnvFileRenderer.Render("", CreateMinimalProfile()));

        Assert.Contains("configFileName", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_NullProfile_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            EnvFileRenderer.Render("run-profiles.yaml", null!));

        Assert.Equal("profile", ex.ParamName);
    }

    [Fact]
    public void Render_WithPlannerProfile_EmitsPlannerSection()
    {
        var profile = CreateMinimalProfile() with
        {
            Planner = new PlannerProfile(
                AspnetcoreUrls: "http://localhost:3004",
                GatewayBaseUrl: "http://localhost:3001/mcp",
                ExecutorHandoffUrl: "http://localhost:3005/a2a/executor",
                ClientId: null,
                ClientSecret: null,
                OAuthAuthority: null,
                OAuthScope: null,
                LlmProvider: null,
                LlmModel: null,
                AnomalyWallClockCapSeconds: null,
                BatchWallClockCapSeconds: null,
                MaxToolIterations: null,
                FileSinkRoot: null,
                PlannerHostPath: null),
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Planner", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.PlannerAspnetcoreUrls}=http://localhost:3004", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.PlannerGatewayBaseUrl}=http://localhost:3001/mcp", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.PlannerExecutorHandoffUrl}=http://localhost:3005/a2a/executor", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithOpenRouterProfile_EmitsOpenRouterSection()
    {
        var profile = CreateMinimalProfile() with
        {
            OpenRouter = new OpenRouterProfile("sk-test"),
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# OpenRouter", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.OpenRouterApiKey}=sk-test", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithExecutorProfile_EmitsExecutorSection()
    {
        var profile = CreateMinimalProfile() with
        {
            Executor = new ExecutorProfile(
                AspnetcoreUrls: "http://localhost:3005",
                GatewayBaseUrl: "http://localhost:3001/mcp",
                ClientId: null,
                ClientSecret: null,
                OAuthAuthority: null,
                OAuthScope: null,
                ConcurrencyCap: "32",
                WatchTimeoutSeconds: "900",
                ExecutorHostPath: null),
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Executor", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ExecutorAspnetcoreUrls}=http://localhost:3005", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ExecutorGatewayBaseUrl}=http://localhost:3001/mcp", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ExecutorConcurrencyCap}=32", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ExecutorWatchTimeoutSeconds}=900", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_NullPlannerProfile_OmitsPlannerSection()
    {
        var profile = CreateMinimalProfile() with { Planner = null };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.DoesNotContain("# Planner", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_NullExecutorProfile_OmitsExecutorSection()
    {
        var profile = CreateMinimalProfile() with { Executor = null };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.DoesNotContain("# Executor", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ContainsHeaderWithConfigAndProfileName()
    {
        var profile = CreateMinimalProfile();

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("run-profiles.yaml", result, StringComparison.Ordinal);
        Assert.Contains(profile.Name, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PlannerProfileAllNulls_OmitsPlannerSection()
    {
        var profile = CreateMinimalProfile() with
        {
            Planner = new PlannerProfile(
                null, null, null, null, null, null, null, null, null, null, null, null, null, null),
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.DoesNotContain("# Planner", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithRuntimeMode_EmitsRuntimeSection()
    {
        var profile = CreateMinimalProfile() with { RuntimeMode = "mcp-stdio" };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Runtime", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.InfraGateEnvironment}=mcp-stdio", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithGatewayProfile_EmitsGatewaySection()
    {
        var profile = CreateMinimalProfile() with
        {
            Gateway = new GatewayProfile("http://localhost:3001", "InfraGate.McpServer.dll", null)
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Gateway", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.AspnetcoreUrls}=http://localhost:3001", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAssembly}=InfraGate.McpServer.dll", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithIdentityProvider_EmitsIdentityProviderSection()
    {
        var profile = CreateMinimalProfile() with
        {
            IdentityProvider = new IdentityProviderProfile(
                RealmImport: null,
                Authority: "http://auth:8080/realms/master",
                MetadataAddress: "http://auth:8080/realms/master/.well-known/openid-configuration",
                Resource: "gateway-client",
                Scope: "openid profile",
                RequireHttpsMetadata: "false",
                TokenIntrospectionEnabled: "true",
                TokenIntrospectionEndpoint: "http://auth:8080/realms/master/protocol/openid-connect/token/introspect",
                TokenIntrospectionClientId: "infra-gate-token-introspection",
                TokenIntrospectionClientSecret: "secret-placeholder",
                TokenIntrospectionCacheSeconds: "15",
                MaxAcceptedAccessTokenLifetimeSeconds: "300")
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Identity Provider", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.OauthAuthority}=http://auth:8080/realms/master", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.TokenIntrospectionEnabled}=true", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.TokenIntrospectionEndpoint}=http://auth:8080/realms/master/protocol/openid-connect/token/introspect", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.TokenIntrospectionClientId}=infra-gate-token-introspection", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.TokenIntrospectionClientSecret}=secret-placeholder", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.TokenIntrospectionCacheSeconds}=15", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.MaxAcceptedAccessTokenLifetimeSeconds}=300", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithApprovalAuthority_EmitsApprovalAuthoritySection()
    {
        var profile = CreateMinimalProfile() with
        {
            ApprovalAuthority = new ApprovalAuthorityProfile(
                "http://gateway.test",
                "approval-client",
                "/approval/callback",
                "http://auth:8080/realms/master/protocol/openid-connect/auth",
                "http://auth:8080/realms/master/protocol/openid-connect/token")
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Approval Authority", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ApprovalBaseUrl}=http://gateway.test", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithGenericApprovalCore_EmitsApprovalRoot()
    {
        var profile = CreateMinimalProfile() with
        {
            GenericApprovalCore = new GenericApprovalCoreProfile("/data/approvals")
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Generic Approval Core", result, StringComparison.Ordinal);
        Assert.Contains("InfraGate__Approval__Root=/data/approvals", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithDownstreamAuth_EmitsDownstreamAuthSection()
    {
        var profile = CreateMinimalProfile() with
        {
            DownstreamAuth = new DownstreamAuthProfile(
                "true",
                null, null, null,
                "http://localhost:3001/mcp",
                "mcp-tools-scope",
                null, null)
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Downstream Auth", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAuthAudience}=http://localhost:3001/mcp", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithKubernetesAdapter_EmitsKubernetesSection()
    {
        var profile = CreateMinimalProfile() with
        {
            DomainAdapters =
            [
                new DomainAdapterProfile("k8s", "kubernetes",
                    new KubernetesAdapterProfile("/run/kube/config", ["ns1", "ns2"]))
            ]
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Kubernetes Adapter", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.KubeConfig}=/run/kube/config", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithHostProfile_EmitsHostSection()
    {
        var profile = CreateMinimalProfile() with
        {
            Host = new HostProfile("0.0.0.0", "8080", "infragate/gateway:latest", null, null, null, null)
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Host", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.BindAddress}=0.0.0.0", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithObserverAllowedNamespaces_EmitsAllowedNamespacesList()
    {
        var profile = CreateMinimalProfile() with
        {
            Observer = new ObserverProfile(
                "http://observer:3002",
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
                ["default", "mcp-nginx-demo"])
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("# Observer", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ObserverAllowedNamespaces}__0=default", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ObserverAllowedNamespaces}__1=mcp-nginx-demo", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_NullGatewayProfile_OmitsGatewaySection()
    {
        var profile = CreateMinimalProfile() with { Gateway = null };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.DoesNotContain("# Gateway", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_NullHostProfile_OmitsHostSection()
    {
        var profile = CreateMinimalProfile() with { Host = null };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.DoesNotContain("# Host", result, StringComparison.Ordinal);
    }

    private static RunProfile CreateMinimalProfile() =>
        new("test-profile", "mcp-stdio", null, null, null, null, null, [], null, null, null, null, null, null);
}
