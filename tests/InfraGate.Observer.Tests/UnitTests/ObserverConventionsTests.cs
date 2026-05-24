namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverConventionsTests
{
    [Fact]
    public void ReadOnlyToolNames_ContainsExpectedCount()
    {
        Assert.Equal(8, ObserverConventions.ToolNames.ReadOnlyToolNames.Count);
    }

    [Fact]
    public void DefaultUrl_UsesPort3003()
    {
        Assert.EndsWith(":3003", ObserverConventions.DefaultUrl);
    }

    [Fact]
    public void ReadOnlyToolNames_ExcludesMutationTools()
    {
        var toolNames = ObserverConventions.ToolNames.ReadOnlyToolNames;

        Assert.DoesNotContain("request_scale_deployment", toolNames);
        Assert.DoesNotContain("execute_approved_plan", toolNames);
        Assert.DoesNotContain("apply_manifest", toolNames);
    }
}
