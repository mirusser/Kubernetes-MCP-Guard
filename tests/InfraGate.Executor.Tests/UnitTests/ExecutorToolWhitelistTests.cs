using InfraGate.Executor.Mcp;

namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorToolWhitelistTests
{
    [Theory]
    [InlineData(ExecutorConventions.ToolNames.WaitForPlanApproval)]
    [InlineData(ExecutorConventions.ToolNames.ExecuteApprovedPlan)]
    public void AssertAllowed_AllowedTool_DoesNotThrow(string toolName)
    {
        var ex = Record.Exception(() => ExecutorToolWhitelist.AssertAllowed(toolName));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("propose_plan")]
    [InlineData("restart_deployment")]
    [InlineData("get_k8s_status")]
    [InlineData("apply_manifest")]
    [InlineData("")]
    public void AssertAllowed_DisallowedTool_Throws(string toolName)
    {
        Assert.Throws<InvalidOperationException>(() => ExecutorToolWhitelist.AssertAllowed(toolName));
    }
}
