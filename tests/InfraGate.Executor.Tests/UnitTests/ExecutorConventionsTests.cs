namespace InfraGate.Executor.Tests.UnitTests;

public sealed class ExecutorConventionsTests
{
    [Fact]
    public void DefaultUrl_ContainsPort3005()
    {
        Assert.Contains(":3005", ExecutorConventions.DefaultUrl);
    }

    [Fact]
    public void HealthEndpointPath_IsSlashHealth()
    {
        Assert.Equal("/health", ExecutorConventions.HealthEndpointPath);
    }

    [Fact]
    public void A2AHandoffEndpointPath_IsCorrect()
    {
        Assert.Equal("/a2a/executor", ExecutorConventions.A2AHandoffEndpointPath);
        Assert.Equal("executor-agent", ExecutorConventions.A2AHandoffAgentName);
    }

    [Fact]
    public void AllowedToolNames_ContainsWaitAndExecute()
    {
        Assert.Contains(ExecutorConventions.ToolNames.WaitForPlanApproval, ExecutorConventions.ToolNames.AllowedToolNames);
        Assert.Contains(ExecutorConventions.ToolNames.ExecuteApprovedPlan, ExecutorConventions.ToolNames.AllowedToolNames);
    }

    [Fact]
    public void AllowedToolNames_ContainsExactlyTwoEntries()
    {
        Assert.Equal(2, ExecutorConventions.ToolNames.AllowedToolNames.Count);
    }

    [Fact]
    public void ConcurrencyCapBounds_AreOrdered()
    {
        Assert.True(ExecutorConventions.MinConcurrencyCap > 0);
        Assert.True(ExecutorConventions.MinConcurrencyCap < ExecutorConventions.DefaultConcurrencyCap);
        Assert.True(ExecutorConventions.DefaultConcurrencyCap < ExecutorConventions.MaxConcurrencyCap);
    }

    [Fact]
    public void WatchTimeoutBounds_AreOrdered()
    {
        Assert.True(ExecutorConventions.MinWatchTimeoutSeconds > 0);
        Assert.True(ExecutorConventions.MinWatchTimeoutSeconds < ExecutorConventions.DefaultWatchTimeoutSeconds);
        Assert.True(ExecutorConventions.DefaultWatchTimeoutSeconds <= ExecutorConventions.MaxWatchTimeoutSeconds);
    }

    [Fact]
    public void PlanStatusValues_CoverAllExpectedStatuses()
    {
        Assert.Equal("NotFound", ExecutorConventions.PlanStatusValues.NotFound);
        Assert.Equal("Approved", ExecutorConventions.PlanStatusValues.Approved);
        Assert.Equal("Applied", ExecutorConventions.PlanStatusValues.Applied);
        Assert.Equal("Expired", ExecutorConventions.PlanStatusValues.Expired);
        Assert.Equal("ApprovalRequired", ExecutorConventions.PlanStatusValues.ApprovalRequired);
    }

    [Fact]
    public void DefaultClientId_IsPinned()
    {
        Assert.Equal("infra-gate-executor", ExecutorConventions.DefaultClientId);
    }

    [Fact]
    public void DefaultOAuthScope_IsPinned()
    {
        Assert.Equal("mcp:tools.execute", ExecutorConventions.DefaultOAuthScope);
    }

    [Fact]
    public void WaitForPlanApprovalPerCallTimeoutSeconds_IsLessThanMinWatchTimeout()
    {
        Assert.True(ExecutorConventions.WaitForPlanApprovalPerCallTimeoutSeconds > 0);
        Assert.True(ExecutorConventions.WaitForPlanApprovalPerCallTimeoutSeconds
            < ExecutorConventions.MinWatchTimeoutSeconds);
    }

    [Fact]
    public void ToolNames_IndividualValues_ArePinned()
    {
        Assert.Equal("wait_for_plan_approval", ExecutorConventions.ToolNames.WaitForPlanApproval);
        Assert.Equal("execute_approved_plan", ExecutorConventions.ToolNames.ExecuteApprovedPlan);
    }

    [Fact]
    public void ToolArguments_ArePinned()
    {
        Assert.Equal("planId", ExecutorConventions.ToolArguments.PlanId);
        Assert.Equal("timeoutSeconds", ExecutorConventions.ToolArguments.TimeoutSeconds);
    }

    [Fact]
    public void EnvironmentVariables_ArePinned()
    {
        Assert.Equal("ASPNETCORE_URLS", ExecutorConventions.EnvironmentVariables.AspNetCoreUrls);
        Assert.Equal("INFRA_GATE_EXECUTOR_GATEWAY_BASE_URL", ExecutorConventions.EnvironmentVariables.GatewayBaseUrl);
        Assert.Equal("INFRA_GATE_EXECUTOR_CONCURRENCY_CAP", ExecutorConventions.EnvironmentVariables.ConcurrencyCap);
        Assert.Equal("INFRA_GATE_EXECUTOR_WATCH_TIMEOUT_SECONDS", ExecutorConventions.EnvironmentVariables.WatchTimeoutSeconds);
        Assert.Equal("INFRA_GATE_EXECUTOR_CLIENT_ID", ExecutorConventions.EnvironmentVariables.ClientId);
        Assert.Equal("INFRA_GATE_EXECUTOR_CLIENT_SECRET", ExecutorConventions.EnvironmentVariables.ClientSecret);
        Assert.Equal("INFRA_GATE_EXECUTOR_OAUTH_AUTHORITY", ExecutorConventions.EnvironmentVariables.OAuthAuthority);
        Assert.Equal("INFRA_GATE_EXECUTOR_OAUTH_SCOPE", ExecutorConventions.EnvironmentVariables.OAuthScope);
    }

    [Fact]
    public void ConfigurationKeys_ArePinned()
    {
        Assert.Equal("InfraGate:Executor", ExecutorConventions.ConfigurationKeys.Executor);
        Assert.Equal("InfraGate:Executor:GatewayBaseUrl", ExecutorConventions.ConfigurationKeys.GatewayBaseUrl);
        Assert.Equal("InfraGate:Executor:ConcurrencyCap", ExecutorConventions.ConfigurationKeys.ConcurrencyCap);
        Assert.Equal("InfraGate:Executor:WatchTimeoutSeconds", ExecutorConventions.ConfigurationKeys.WatchTimeoutSeconds);
    }

    [Fact]
    public void Claims_AuthorizedParty_IsPinned()
    {
        Assert.Equal("azp", ExecutorConventions.Claims.AuthorizedParty);
    }

    [Fact]
    public void ServiceClients_Planner_IsPinned()
    {
        Assert.Equal("infra-gate-planner", ExecutorConventions.ServiceClients.Planner);
    }

    [Fact]
    public void Policies_PlannerSender_IsPinned()
    {
        Assert.Equal("PlannerSender", ExecutorConventions.Policies.PlannerSender);
    }
}
