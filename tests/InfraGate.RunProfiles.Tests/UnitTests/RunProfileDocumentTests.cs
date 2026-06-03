namespace InfraGate.RunProfiles.Tests.UnitTests;

public sealed class RunProfileDocumentTests
{
    [Fact]
    public void FindProfile_KnownName_ReturnsProfile()
    {
        var profile = MakeProfile("my-profile");
        var doc = new RunProfileDocument([profile]);

        var found = doc.FindProfile("my-profile");

        Assert.Same(profile, found);
    }

    [Fact]
    public void FindProfile_UnknownName_ThrowsInvalidOperationException()
    {
        var doc = new RunProfileDocument([MakeProfile("my-profile")]);

        Assert.Throws<InvalidOperationException>(() => doc.FindProfile("other-profile"));
    }

    [Fact]
    public void FindProfileWithDefaults_NullDefaults_ReturnsOriginalProfile()
    {
        var profile = MakeProfile("my-profile");
        var doc = new RunProfileDocument([profile]);

        var found = doc.FindProfileWithDefaults("my-profile", null);

        Assert.Equal(profile, found);
    }

    [Fact]
    public void FindProfileWithDefaults_GatewayDefaults_MergesUrlsWhenProfileHasNone()
    {
        var profile = MakeProfile("my-profile") with
        {
            Gateway = new GatewayProfile(null, null, null)
        };
        var defaults = new ProfileDefaults(
            Gateway: new GatewayProfile("http://localhost:3001", null, null),
            null, null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Equal("http://localhost:3001", merged.Gateway?.AspnetcoreUrls);
    }

    [Fact]
    public void FindProfileWithDefaults_GatewayDefaults_ProfileValueTakesPrecedence()
    {
        var profile = MakeProfile("my-profile") with
        {
            Gateway = new GatewayProfile("http://localhost:4000", null, null)
        };
        var defaults = new ProfileDefaults(
            Gateway: new GatewayProfile("http://localhost:3001", null, null),
            null, null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Equal("http://localhost:4000", merged.Gateway?.AspnetcoreUrls);
    }

    [Fact]
    public void FindProfileWithDefaults_NullGatewayProfile_DefaultsDoNotMaterialize()
    {
        var profile = MakeProfile("my-profile");
        var defaults = new ProfileDefaults(
            Gateway: new GatewayProfile("http://localhost:3001", null, null),
            null, null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Null(merged.Gateway);
    }

    [Fact]
    public void Profiles_ReflectsConstructorInput()
    {
        var profiles = new[]
        {
            MakeProfile("alpha"),
            MakeProfile("beta"),
        };
        var doc = new RunProfileDocument(profiles);

        Assert.Equal(2, doc.Profiles.Count);
        Assert.Equal("alpha", doc.Profiles[0].Name);
        Assert.Equal("beta", doc.Profiles[1].Name);
    }

    [Fact]
    public void Defaults_InitializerSetsDefaults()
    {
        var defaults = new ProfileDefaults(null, null, null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([MakeProfile("x")]) { Defaults = defaults };

        Assert.Same(defaults, doc.Defaults);
    }

    [Fact]
    public void FindProfileWithDefaults_AgentDefaults_MergesMissingValues()
    {
        var profile = MakeProfile("my-profile") with
        {
            Observer = new ObserverProfile(
                AspnetcoreUrls: "http://observer-local",
                GatewayBaseUrl: null,
                OAuthAuthority: null,
                ClientId: null,
                ClientSecret: null,
                Scope: null,
                LlmProvider: null,
                LlmModel: null,
                CycleCadenceSeconds: null,
                CycleWallClockCapSeconds: null,
                MaxToolIterations: null,
                FileSinkRoot: null,
                PlannerHandoffUrl: null,
                ObserverHostPath: null,
                AuditConnectionString: null,
                SkipCycleWhenNoWarningEvents: null,
                AllowedNamespaces: null),
            Planner = new PlannerProfile(
                AspnetcoreUrls: null,
                GatewayBaseUrl: "http://gateway-local",
                ExecutorHandoffUrl: null,
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
            Executor = new ExecutorProfile(
                null, null, "executor-local", null, null, null, null, null, null)
        };
        var defaults = new ProfileDefaults(
            null, null, null, null, null, null, null,
            new ObserverProfile(
                "http://observer-default",
                "http://gateway-default",
                "http://authority-default",
                "observer-client",
                "observer-secret",
                "mcp:tools.readonly",
                "openai",
                "gpt-test",
                "30",
                "20",
                "5",
                "/observer/out",
                "http://planner/handoff",
                "/observer/state",
                null,
                null,
                ["default"]),
            new PlannerProfile(
                "http://planner-default",
                "http://planner-gateway-default",
                "http://executor/handoff",
                "planner-client",
                "planner-secret",
                "http://planner-authority",
                "mcp:tools.propose",
                "openai",
                "gpt-planner",
                "15",
                "45",
                "8",
                "/planner/out",
                "/planner/state"),
            new ExecutorProfile(
                "http://executor-default",
                "http://executor-gateway-default",
                "executor-default",
                "executor-secret",
                "http://executor-authority",
                "mcp:tools.execute",
                "2",
                "120",
                "/executor/state"));
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Equal("http://observer-local", merged.Observer?.AspnetcoreUrls);
        Assert.Equal("http://gateway-default", merged.Observer?.GatewayBaseUrl);
        Assert.Equal("http://gateway-local", merged.Planner?.GatewayBaseUrl);
        Assert.Equal("http://executor/handoff", merged.Planner?.ExecutorHandoffUrl);
        Assert.Equal("executor-local", merged.Executor?.ClientId);
        Assert.Equal("http://executor-default", merged.Executor?.AspnetcoreUrls);
    }

    [Fact]
    public void FindProfileWithDefaults_NullAgentProfiles_DefaultsDoNotMaterialize()
    {
        var profile = MakeProfile("my-profile");
        var defaults = new ProfileDefaults(
            null, null, null, null, null, null, null,
            new ObserverProfile(
                "http://observer",
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null),
            new PlannerProfile(
                "http://planner",
                null, null, null, null, null, null, null, null, null, null, null, null, null),
            new ExecutorProfile("http://executor", null, null, null, null, null, null, null, null));
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Null(merged.Observer);
        Assert.Null(merged.Planner);
        Assert.Null(merged.Executor);
    }

    [Fact]
    public void FindProfileWithDefaults_IdentityProviderDefaults_MergesValues()
    {
        var profile = MakeProfile("my-profile") with
        {
            IdentityProvider = new IdentityProviderProfile(null, null, null, null, null, null)
        };
        var defaults = new ProfileDefaults(
            null,
            new IdentityProviderProfile(null, "http://auth:8080", null, null, null, null),
            null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.NotNull(merged.IdentityProvider);
        Assert.Equal("http://auth:8080", merged.IdentityProvider!.Authority);
    }

    [Fact]
    public void FindProfileWithDefaults_ApprovalAuthorityDefaults_MergesValues()
    {
        var profile = MakeProfile("my-profile") with
        {
            ApprovalAuthority = new ApprovalAuthorityProfile(null, null, null, null, null)
        };
        var defaults = new ProfileDefaults(
            null, null,
            new ApprovalAuthorityProfile("http://gateway.test", null, null, null, null),
            null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.NotNull(merged.ApprovalAuthority);
        Assert.Equal("http://gateway.test", merged.ApprovalAuthority!.BaseUrl);
    }

    [Fact]
    public void FindProfileWithDefaults_HostDefaults_MergesValues()
    {
        var profile = MakeProfile("my-profile") with
        {
            Host = new HostProfile(null, null, null, null, null, null, null)
        };
        var defaults = new ProfileDefaults(
            null, null, null, null,
            new HostProfile("0.0.0.0", "8080", null, null, null, null, null),
            null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.NotNull(merged.Host);
        Assert.Equal("0.0.0.0", merged.Host!.BindAddress);
        Assert.Equal("8080", merged.Host!.BindPort);
    }

    [Fact]
    public void FindProfileWithDefaults_DownstreamAuthDefaults_MergesValues()
    {
        var profile = MakeProfile("my-profile") with { DownstreamAuth = null };
        var defaults = new ProfileDefaults(
            null, null, null, null, null,
            new DownstreamAuthProfile("true", null, null, null, "audience-value", null, null, null),
            null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.NotNull(merged.DownstreamAuth);
        Assert.Equal("true", merged.DownstreamAuth!.Required);
        Assert.Equal("audience-value", merged.DownstreamAuth!.Audience);
    }

    [Fact]
    public void FindProfileWithDefaults_GenericApprovalCoreDefaults_MergesValues()
    {
        var profile = MakeProfile("my-profile") with { GenericApprovalCore = null };
        var defaults = new ProfileDefaults(
            null, null, null,
            new GenericApprovalCoreProfile("/data/approvals", "Host=db;Database=test"),
            null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.NotNull(merged.GenericApprovalCore);
        Assert.Equal("/data/approvals", merged.GenericApprovalCore!.ApprovalRoot);
        Assert.Equal("Host=db;Database=test", merged.GenericApprovalCore!.PostgresConnectionString);
    }

    [Fact]
    public void FindProfileWithDefaults_OpenRouterProfileMissing_UsesDefaults()
    {
        var profile = MakeProfile("my-profile") with { OpenRouter = null };
        var defaults = new ProfileDefaults(
            null, null, null, null, null, null,
            new OpenRouterProfile("default-openrouter-key"),
            null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.NotNull(merged.OpenRouter);
        Assert.Equal("default-openrouter-key", merged.OpenRouter!.ApiKey);
    }

    [Fact]
    public void FindProfileWithDefaults_OpenRouterDefaults_ProfileValueTakesPrecedence()
    {
        var profile = MakeProfile("my-profile") with
        {
            OpenRouter = new OpenRouterProfile("profile-openrouter-key")
        };
        var defaults = new ProfileDefaults(
            null, null, null, null, null, null,
            new OpenRouterProfile("default-openrouter-key"),
            null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Equal("profile-openrouter-key", merged.OpenRouter?.ApiKey);
    }

    [Fact]
    public void FindProfileWithDefaults_OpenRouterDefaults_MergesMissingApiKey()
    {
        var profile = MakeProfile("my-profile") with
        {
            OpenRouter = new OpenRouterProfile(null)
        };
        var defaults = new ProfileDefaults(
            null, null, null, null, null, null,
            new OpenRouterProfile("default-openrouter-key"),
            null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Equal("default-openrouter-key", merged.OpenRouter?.ApiKey);
    }

    [Fact]
    public void FindProfileWithDefaults_OpenRouterDefaultsMissing_ReturnsProfileValue()
    {
        var profile = MakeProfile("my-profile") with
        {
            OpenRouter = new OpenRouterProfile("profile-openrouter-key")
        };
        var defaults = new ProfileDefaults(null, null, null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Same(profile.OpenRouter, merged.OpenRouter);
    }

    private static RunProfile MakeProfile(string name) =>
        new(name, "mcp-stdio", null, null, null, null, null, [], null, null, null, null, null, null);
}
