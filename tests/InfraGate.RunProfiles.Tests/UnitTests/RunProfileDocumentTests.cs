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
            null, null, null, null, null, null, null, null);
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
            null, null, null, null, null, null, null, null);
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
            null, null, null, null, null, null, null, null);
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
        var defaults = new ProfileDefaults(null, null, null, null, null, null, null, null, null);
        var doc = new RunProfileDocument([MakeProfile("x")]) { Defaults = defaults };

        Assert.Same(defaults, doc.Defaults);
    }

    [Fact]
    public void FindProfileWithDefaults_AgentDefaults_MergesMissingValues()
    {
        var profile = MakeProfile("my-profile") with
        {
            Observer = new ObserverProfile(
                "http://observer-local",
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null),
            Planner = new PlannerProfile(
                null,
                "http://gateway-local",
                null, null, null, null, null, null, null, null, null, null, null, null, null, null),
            Executor = new ExecutorProfile(
                null, null, null, "executor-local", null, null, null, null, null, null)
        };
        var defaults = new ProfileDefaults(
            null, null, null, null, null, null,
            new ObserverProfile(
                "http://observer-default",
                "http://gateway-default",
                "http://token-default",
                "http://authority-default",
                "observer-client",
                "observer-secret",
                "mcp:tools.readonly",
                "openai",
                "gpt-test",
                "observer-key",
                "30",
                "20",
                "5",
                "/observer/out",
                "http://planner/handoff",
                "/observer/state",
                ["default"]),
            new PlannerProfile(
                "http://planner-default",
                "http://planner-gateway-default",
                "http://executor/handoff",
                "http://planner/token",
                "planner-client",
                "planner-secret",
                "http://planner-authority",
                "mcp:tools.propose",
                "openai",
                "gpt-planner",
                "planner-key",
                "15",
                "45",
                "8",
                "/planner/out",
                "/planner/state"),
            new ExecutorProfile(
                "http://executor-default",
                "http://executor-gateway-default",
                "http://executor/token",
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
            null, null, null, null, null, null,
            new ObserverProfile(
                "http://observer",
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null),
            new PlannerProfile(
                "http://planner",
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null),
            new ExecutorProfile("http://executor", null, null, null, null, null, null, null, null, null));
        var doc = new RunProfileDocument([profile]);

        var merged = doc.FindProfileWithDefaults("my-profile", defaults);

        Assert.Null(merged.Observer);
        Assert.Null(merged.Planner);
        Assert.Null(merged.Executor);
    }

    private static RunProfile MakeProfile(string name) =>
        new(name, "mcp-stdio", null, null, null, null, null, [], null, null, null, null, null);
}
