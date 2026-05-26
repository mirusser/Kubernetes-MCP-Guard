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
                ExecutorHandoffUrl: "http://localhost:3005/handoff/proposals",
                TokenEndpoint: null,
                ClientId: null,
                ClientSecret: null,
                OAuthAuthority: null,
                OAuthScope: null,
                LlmProvider: null,
                LlmModel: null,
                LlmApiKey: "sk-test",
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
        Assert.Contains($"{RunProfileConventions.Env.PlannerExecutorHandoffUrl}=http://localhost:3005/handoff/proposals", result, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.PlannerLlmApiKey}=sk-test", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithExecutorProfile_EmitsExecutorSection()
    {
        var profile = CreateMinimalProfile() with
        {
            Executor = new ExecutorProfile(
                AspnetcoreUrls: "http://localhost:3005",
                GatewayBaseUrl: "http://localhost:3001/mcp",
                TokenEndpoint: null,
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
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null),
        };

        string result = EnvFileRenderer.Render("run-profiles.yaml", profile);

        Assert.DoesNotContain("# Planner", result, StringComparison.Ordinal);
    }

    private static RunProfile CreateMinimalProfile() =>
        new("test-profile", "mcp-stdio", null, null, null, null, null, [], null, null, null, null, null);
}
