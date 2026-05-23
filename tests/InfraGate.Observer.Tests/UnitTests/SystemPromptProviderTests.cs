using InfraGate.Observer.Prompts;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class SystemPromptProviderTests
{
    [Fact]
    public void Get_SubstitutesNamespacePlaceholder()
    {
        var provider = new SystemPromptProvider();
        var prompt = provider.Get("mcp-nginx-demo", 8);

        Assert.DoesNotContain("{NAMESPACE}", prompt, StringComparison.Ordinal);
        Assert.Contains("mcp-nginx-demo", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_SubstitutesMaxToolIterationsPlaceholder()
    {
        var provider = new SystemPromptProvider();
        var prompt = provider.Get("test-ns", 3);

        Assert.DoesNotContain("{MAX_TOOL_ITERATIONS}", prompt, StringComparison.Ordinal);
        Assert.Contains("3 tool calls", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_KnownPlaceholdersAreReplaced()
    {
        var provider = new SystemPromptProvider();
        var prompt = provider.Get("default", 8);

        Assert.DoesNotContain("{NAMESPACE}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{MAX_TOOL_ITERATIONS}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_ForbidsMutationToolNames()
    {
        var provider = new SystemPromptProvider();
        var prompt = provider.Get("default", 8);

        Assert.Contains("request_", prompt, StringComparison.Ordinal);
        Assert.Contains("execute_", prompt, StringComparison.Ordinal);
        Assert.Contains("apply_", prompt, StringComparison.Ordinal);
        Assert.Contains("delete_", prompt, StringComparison.Ordinal);
        Assert.Contains("scale_", prompt, StringComparison.Ordinal);
        Assert.Contains("restart_", prompt, StringComparison.Ordinal);
        Assert.Contains("set_", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_ContainsAllFourAnomalyKinds()
    {
        var provider = new SystemPromptProvider();
        var prompt = provider.Get("default", 8);

        Assert.Contains("PodUnhealthy", prompt, StringComparison.Ordinal);
        Assert.Contains("DeploymentUnavailable", prompt, StringComparison.Ordinal);
        Assert.Contains("ServiceNoEndpoints", prompt, StringComparison.Ordinal);
        Assert.Contains("WarningEvent", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_ContainsAllReadOnlyTools()
    {
        var provider = new SystemPromptProvider();
        var prompt = provider.Get("default", 8);

        foreach (var toolName in ObserverConventions.ToolNames.ReadOnlyToolNames)
        {
            Assert.Contains(toolName, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Get_IsDeterministicForSameInput()
    {
        var provider = new SystemPromptProvider();
        var first = provider.Get("ns1", 5);
        var second = provider.Get("ns1", 5);

        Assert.Equal(first, second, StringComparer.Ordinal);
    }

    [Fact]
    public void Get_DifferentNamespacesProduceDifferentOutput()
    {
        var provider = new SystemPromptProvider();
        var first = provider.Get("ns-a", 5);
        var second = provider.Get("ns-b", 5);

        Assert.NotEqual(first, second, StringComparer.Ordinal);
    }
}
