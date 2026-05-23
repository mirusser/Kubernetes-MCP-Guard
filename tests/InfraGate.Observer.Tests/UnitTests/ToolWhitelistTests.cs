using InfraGate.Observer.Mcp;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ToolWhitelistTests
{
    [Theory]
    [InlineData("get_allowed_namespaces")]
    [InlineData("get_k8s_status")]
    [InlineData("get_k8s_events")]
    [InlineData("get_k8s_pods")]
    [InlineData("describe_k8s_resource")]
    [InlineData("get_k8s_deployments")]
    [InlineData("get_k8s_services")]
    [InlineData("get_k8s_endpoints")]
    public void AssertAllowed_ReadOnlyTool_DoesNotThrow(string toolName)
    {
        var exception = Record.Exception(() => ToolWhitelist.AssertAllowed(toolName));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("request_scale_deployment")]
    [InlineData("execute_approved_plan")]
    [InlineData("apply_manifest")]
    [InlineData("delete_resource")]
    [InlineData("restart_deployment")]
    [InlineData("set_image")]
    [InlineData("unknown_tool")]
    public void AssertAllowed_MutationTool_Throws(string toolName)
    {
        Assert.Throws<InvalidOperationException>(() => ToolWhitelist.AssertAllowed(toolName));
    }

    [Fact]
    public void AssertAllowed_EmptyString_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ToolWhitelist.AssertAllowed(""));
    }

    [Fact]
    public void AssertAllowed_Null_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => ToolWhitelist.AssertAllowed(null!));
    }
}
