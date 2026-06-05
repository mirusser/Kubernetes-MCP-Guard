using System.Security.Claims;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ToolScopeCatalogTests
{
    [Fact]
    public void GetSynthesizedScopes_RequestTools_IncludesWriteAndMutationScopes()
    {
        var scopes = ToolScopeCatalog.GetSynthesizedScopes("request_scale_deployment");

        Assert.NotNull(scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.MutationScope, scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.WriteScope, scopes);
    }

    [Fact]
    public void GetSynthesizedScopes_RequestTools_DoesNotIncludeReadScope()
    {
        var scopes = ToolScopeCatalog.GetSynthesizedScopes("request_restart_deployment");

        Assert.NotNull(scopes);
        Assert.DoesNotContain(McpGatewayConventions.ToolScopeRequirements.ReadScope, scopes);
    }

    [Fact]
    public void GetSynthesizedScopes_ExecuteApprovedPlan_IncludesWriteScope()
    {
        var scopes = ToolScopeCatalog.GetSynthesizedScopes(McpGatewayConventions.ToolNames.ApplyApprovedPlan);

        Assert.NotNull(scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.ExecuteScope, scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.WriteScope, scopes);
    }

    [Fact]
    public void GetSynthesizedScopes_GetPlanStatus_IncludesReadScope()
    {
        var scopes = ToolScopeCatalog.GetSynthesizedScopes(McpGatewayConventions.ToolNames.GetPlanStatus);

        Assert.NotNull(scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.ReadOnlyScope, scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.ReadScope, scopes);
    }

    [Fact]
    public void GetSynthesizedScopes_GetPlanStatus_DoesNotIncludeWriteScope()
    {
        var scopes = ToolScopeCatalog.GetSynthesizedScopes(McpGatewayConventions.ToolNames.GetPlanStatus);

        Assert.NotNull(scopes);
        Assert.DoesNotContain(McpGatewayConventions.ToolScopeRequirements.WriteScope, scopes);
    }

    [Fact]
    public void GetSynthesizedScopes_WaitForPlanApproval_IncludesWriteScope()
    {
        var scopes = ToolScopeCatalog.GetSynthesizedScopes(McpGatewayConventions.ToolNames.WaitForPlanApproval);

        Assert.NotNull(scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.ExecuteScope, scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.WriteScope, scopes);
    }

    [Fact]
    public void GetSynthesizedScopes_ProposePlan_IncludesWriteScope()
    {
        var scopes = ToolScopeCatalog.GetSynthesizedScopes(McpGatewayConventions.ToolNames.ProposePlan);

        Assert.NotNull(scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.ProposeScope, scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.WriteScope, scopes);
    }

    [Fact]
    public void GetRequiredScopes_DownstreamReadOnly_IncludesReadScope()
    {
        var scopes = ToolScopeCatalog.GetRequiredScopes("get_k8s_status", hasReadOnlyHint: true);

        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.ReadOnlyScope, scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.ReadScope, scopes);
    }

    [Fact]
    public void GetRequiredScopes_DownstreamDestructive_IncludesWriteScope()
    {
        var scopes = ToolScopeCatalog.GetRequiredScopes("delete_something", hasReadOnlyHint: false);

        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.MutationScope, scopes);
        Assert.Contains(McpGatewayConventions.ToolScopeRequirements.WriteScope, scopes);
    }

    [Fact]
    public void IsVisibleTo_ReadScopeUser_HidesRequestTools()
    {
        var user = CreateUserWithScopes(McpGatewayConventions.ToolScopeRequirements.ReadScope);

        var visible = ToolScopeCatalog.IsVisibleTo("request_scale_deployment", hasReadOnlyHint: false, user);

        Assert.False(visible);
    }

    [Fact]
    public void IsVisibleTo_ReadScopeUser_ShowsReadOnlyDownstreamTools()
    {
        var user = CreateUserWithScopes(McpGatewayConventions.ToolScopeRequirements.ReadScope);

        var visible = ToolScopeCatalog.IsVisibleTo("get_k8s_status", hasReadOnlyHint: true, user);

        Assert.True(visible);
    }

    [Fact]
    public void IsVisibleTo_ReadScopeUser_ShowsGetPlanStatus()
    {
        var user = CreateUserWithScopes(McpGatewayConventions.ToolScopeRequirements.ReadScope);

        var visible = ToolScopeCatalog.IsVisibleTo(McpGatewayConventions.ToolNames.GetPlanStatus, hasReadOnlyHint: false, user);

        Assert.True(visible);
    }

    [Fact]
    public void IsVisibleTo_WriteScopeUser_ShowsRequestTools()
    {
        var user = CreateUserWithScopes(McpGatewayConventions.ToolScopeRequirements.WriteScope);

        var visible = ToolScopeCatalog.IsVisibleTo("request_scale_deployment", hasReadOnlyHint: false, user);

        Assert.True(visible);
    }

    [Fact]
    public void IsVisibleTo_WriteScopeUser_ShowsExecuteApprovedPlan()
    {
        var user = CreateUserWithScopes(McpGatewayConventions.ToolScopeRequirements.WriteScope);

        var visible = ToolScopeCatalog.IsVisibleTo(McpGatewayConventions.ToolNames.ApplyApprovedPlan, hasReadOnlyHint: false, user);

        Assert.True(visible);
    }

    [Fact]
    public void IsVisibleTo_MutationScopeUser_SeesEverything()
    {
        var user = CreateUserWithScopes(McpGatewayConventions.ToolScopeRequirements.MutationScope);

        Assert.True(ToolScopeCatalog.IsVisibleTo("request_scale_deployment", hasReadOnlyHint: false, user));
        Assert.True(ToolScopeCatalog.IsVisibleTo(McpGatewayConventions.ToolNames.GetPlanStatus, hasReadOnlyHint: false, user));
        Assert.True(ToolScopeCatalog.IsVisibleTo(McpGatewayConventions.ToolNames.ApplyApprovedPlan, hasReadOnlyHint: false, user));
        Assert.True(ToolScopeCatalog.IsVisibleTo("get_k8s_status", hasReadOnlyHint: true, user));
    }

    [Fact]
    public void GetSynthesizedScopes_UnknownTool_ReturnsNull()
    {
        var scopes = ToolScopeCatalog.GetSynthesizedScopes("nonexistent_tool");

        Assert.Null(scopes);
    }

    private static ClaimsPrincipal CreateUserWithScopes(params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new(GatewayAuthConventions.Claims.Subject, "test-user")
        };
        claims.AddRange(scopes.Select(scope => new Claim(GatewayAuthConventions.Claims.Scope, scope)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
