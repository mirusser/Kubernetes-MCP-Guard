using InfraGate.Planner.Mcp;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerToolWhitelistTests
{
    [Theory]
    [InlineData(PlannerConventions.ToolNames.ProposePlan)]
    [InlineData(PlannerConventions.ToolNames.GetAllowedNamespaces)]
    [InlineData(PlannerConventions.ToolNames.GetK8sStatus)]
    [InlineData(PlannerConventions.ToolNames.GetK8sEvents)]
    [InlineData(PlannerConventions.ToolNames.GetK8sPods)]
    [InlineData(PlannerConventions.ToolNames.DescribeK8sResource)]
    [InlineData(PlannerConventions.ToolNames.GetK8sDeployments)]
    [InlineData(PlannerConventions.ToolNames.GetK8sServices)]
    [InlineData(PlannerConventions.ToolNames.GetK8sEndpoints)]
    public void AssertAllowed_AllowedTool_DoesNotThrow(string toolName)
    {
        var ex = Record.Exception(() => PlannerToolWhitelist.AssertAllowed(toolName));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("execute_approved_plan")]
    [InlineData("wait_for_plan_approval")]
    [InlineData("request_scale_deployment")]
    [InlineData("request_restart_deployment")]
    [InlineData("apply_manifest")]
    [InlineData("delete_resource")]
    [InlineData("set_image")]
    public void AssertAllowed_BlockedTool_ThrowsInvalidOperationException(string toolName)
    {
        Assert.Throws<InvalidOperationException>(() => PlannerToolWhitelist.AssertAllowed(toolName));
    }

    [Fact]
    public void AllowedToolNames_ContainsProposePlan()
    {
        Assert.Contains(PlannerConventions.ToolNames.ProposePlan, PlannerConventions.ToolNames.AllowedToolNames);
    }

    [Fact]
    public void AllowedToolNames_ContainsAllReadOnlyTools()
    {
        string[] readOnlyTools =
        [
            PlannerConventions.ToolNames.GetAllowedNamespaces,
            PlannerConventions.ToolNames.GetK8sStatus,
            PlannerConventions.ToolNames.GetK8sEvents,
            PlannerConventions.ToolNames.GetK8sPods,
            PlannerConventions.ToolNames.DescribeK8sResource,
            PlannerConventions.ToolNames.GetK8sDeployments,
            PlannerConventions.ToolNames.GetK8sServices,
            PlannerConventions.ToolNames.GetK8sEndpoints,
        ];

        foreach (string tool in readOnlyTools)
        {
            Assert.Contains(tool, PlannerConventions.ToolNames.AllowedToolNames);
        }
    }
}
