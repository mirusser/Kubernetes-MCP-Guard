namespace InfraGate.AgentGuardrails.Tests.UnitTests;

public sealed class AgentGuardrailPolicyTests
{
    [Fact]
    public void Constructor_WithAllowedTools_StoresTools()
    {
        var tools = new HashSet<string>(StringComparer.Ordinal) { "get_pods", "get_services" };
        var policy = new AgentGuardrailPolicy(tools);

        Assert.Contains("get_pods", policy.AllowedToolNames);
        Assert.Contains("get_services", policy.AllowedToolNames);
        Assert.Equal(2, policy.AllowedToolNames.Count);
    }

    [Fact]
    public void Constructor_EmptySet_StoresEmptySet()
    {
        var policy = new AgentGuardrailPolicy(new HashSet<string>(StringComparer.Ordinal));

        Assert.Empty(policy.AllowedToolNames);
    }

    [Fact]
    public void AllowedToolNames_ReturnsSameInstance_AsPassedIn()
    {
        var tools = new HashSet<string>(StringComparer.Ordinal) { "read" };
        var policy = new AgentGuardrailPolicy(tools);

        Assert.Same(tools, policy.AllowedToolNames);
    }
}