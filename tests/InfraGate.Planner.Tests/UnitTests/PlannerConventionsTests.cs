namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerConventionsTests
{
    [Fact]
    public void DefaultUrl_UsesPort3004()
    {
        Assert.EndsWith(":3004", PlannerConventions.DefaultUrl);
    }

    [Fact]
    public void HealthEndpointPath_IsSlashHealth()
    {
        Assert.Equal("/health", PlannerConventions.HealthEndpointPath);
    }

    [Fact]
    public void A2AHandoffEndpointPath_IsSlashA2APlanner()
    {
        Assert.Equal("/a2a/planner", PlannerConventions.A2AHandoffEndpointPath);
    }

    [Fact]
    public void A2AHandoffAgentName_IsPlannerAgent()
    {
        Assert.Equal("planner-agent", PlannerConventions.A2AHandoffAgentName);
    }

    [Fact]
    public void DefaultLlmModel_IsSonnet46()
    {
        Assert.Equal("claude-sonnet-4-6", PlannerConventions.DefaultLlmModel);
    }

    [Fact]
    public void AllowedOperationTypes_ContainsRestartScaleSetImage()
    {
        Assert.Contains(PlannerConventions.OperationTypes.RestartDeployment, PlannerConventions.OperationTypes.AllowedOperationTypes);
        Assert.Contains(PlannerConventions.OperationTypes.ScaleDeployment, PlannerConventions.OperationTypes.AllowedOperationTypes);
        Assert.Contains(PlannerConventions.OperationTypes.SetDeploymentImage, PlannerConventions.OperationTypes.AllowedOperationTypes);
    }

    [Fact]
    public void AllowedOperationTypes_ContainsExactlyThreeEntries()
    {
        Assert.Equal(3, PlannerConventions.OperationTypes.AllowedOperationTypes.Count);
    }

    [Fact]
    public void Dedupe_ActivePlanTtl_IsOneHour()
    {
        Assert.Equal(TimeSpan.FromHours(1), PlannerConventions.Dedupe.ActivePlanTtl);
    }

    [Fact]
    public void Dedupe_FailedProposalBackoff_IsFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), PlannerConventions.Dedupe.FailedProposalBackoff);
    }

    [Theory]
    [InlineData(PlannerConventions.DefaultAnomalyWallClockCapSeconds)]
    [InlineData(PlannerConventions.MinAnomalyWallClockCapSeconds)]
    [InlineData(PlannerConventions.MaxAnomalyWallClockCapSeconds)]
    public void AnomalyWallClockCapBounds_ArePositive(int value)
    {
        Assert.True(value > 0);
    }

    [Fact]
    public void AnomalyWallClockCapBounds_MinLessThanDefault_DefaultLessThanMax()
    {
        Assert.True(PlannerConventions.MinAnomalyWallClockCapSeconds
            < PlannerConventions.DefaultAnomalyWallClockCapSeconds);
        Assert.True(PlannerConventions.DefaultAnomalyWallClockCapSeconds
            < PlannerConventions.MaxAnomalyWallClockCapSeconds);
    }

    [Fact]
    public void DefaultClientId_IsPinned()
    {
        Assert.Equal("infra-gate-planner", PlannerConventions.DefaultClientId);
    }

    [Fact]
    public void DefaultOAuthScope_IsPinned()
    {
        Assert.Equal("mcp:tools.propose mcp:tools.readonly", PlannerConventions.DefaultOAuthScope);
    }

    [Fact]
    public void BatchWallClockCapBounds_ArePositiveAndOrdered()
    {
        Assert.True(PlannerConventions.MinBatchWallClockCapSeconds > 0);
        Assert.True(PlannerConventions.MinBatchWallClockCapSeconds
            < PlannerConventions.DefaultBatchWallClockCapSeconds);
        Assert.True(PlannerConventions.DefaultBatchWallClockCapSeconds
            < PlannerConventions.MaxBatchWallClockCapSeconds);
    }

    [Fact]
    public void MaxToolIterationsBounds_ArePositiveAndOrdered()
    {
        Assert.True(PlannerConventions.MinMaxToolIterations > 0);
        Assert.True(PlannerConventions.MinMaxToolIterations
            < PlannerConventions.DefaultMaxToolIterations);
        Assert.True(PlannerConventions.DefaultMaxToolIterations
            < PlannerConventions.MaxMaxToolIterations);
    }

    [Fact]
    public void EnvironmentVariables_ArePinned()
    {
        Assert.Equal("ASPNETCORE_URLS", PlannerConventions.EnvironmentVariables.AspNetCoreUrls);
        Assert.Equal("INFRA_GATE_PLANNER_GATEWAY_BASE_URL", PlannerConventions.EnvironmentVariables.GatewayBaseUrl);
        Assert.Equal("INFRA_GATE_PLANNER_EXECUTOR_HANDOFF_URL", PlannerConventions.EnvironmentVariables.ExecutorHandoffUrl);
        Assert.Equal("INFRA_GATE_PLANNER_LLM_PROVIDER", PlannerConventions.EnvironmentVariables.LlmProvider);
        Assert.Equal("INFRA_GATE_PLANNER_LLM_MODEL", PlannerConventions.EnvironmentVariables.LlmModel);
        Assert.Equal("INFRA_GATE_PLANNER_LLM_API_KEY", PlannerConventions.EnvironmentVariables.LlmApiKey);
        Assert.Equal("INFRA_GATE_PLANNER_CLIENT_ID", PlannerConventions.EnvironmentVariables.ClientId);
        Assert.Equal("INFRA_GATE_PLANNER_CLIENT_SECRET", PlannerConventions.EnvironmentVariables.ClientSecret);
        Assert.Equal("INFRA_GATE_PLANNER_OAUTH_AUTHORITY", PlannerConventions.EnvironmentVariables.OAuthAuthority);
        Assert.Equal("INFRA_GATE_PLANNER_OAUTH_SCOPE", PlannerConventions.EnvironmentVariables.OAuthScope);
        Assert.Equal("INFRA_GATE_PLANNER_FILE_SINK_ROOT", PlannerConventions.EnvironmentVariables.FileSinkRoot);
    }

    [Fact]
    public void ConfigurationKeys_ArePinned()
    {
        Assert.Equal("InfraGate:Planner", PlannerConventions.ConfigurationKeys.Planner);
        Assert.Equal("InfraGate:Planner:GatewayBaseUrl", PlannerConventions.ConfigurationKeys.GatewayBaseUrl);
        Assert.Equal("InfraGate:Planner:ExecutorHandoffUrl", PlannerConventions.ConfigurationKeys.ExecutorHandoffUrl);
        Assert.Equal("InfraGate:Planner:LlmProvider", PlannerConventions.ConfigurationKeys.LlmProvider);
        Assert.Equal("InfraGate:Planner:LlmModel", PlannerConventions.ConfigurationKeys.LlmModel);
        Assert.Equal("InfraGate:Planner:FileSink:Root", PlannerConventions.ConfigurationKeys.FileSinkRoot);
    }

    [Fact]
    public void LlmProviders_ArePinned()
    {
        Assert.Equal("ANTHROPIC", PlannerConventions.LlmProviders.Anthropic);
        Assert.Equal("OPENAI", PlannerConventions.LlmProviders.OpenAI);
        Assert.Equal("GOOGLE", PlannerConventions.LlmProviders.Google);
        Assert.Equal("AZURE", PlannerConventions.LlmProviders.Azure);
        Assert.Equal("OLLAMA", PlannerConventions.LlmProviders.Ollama);
        Assert.Equal("OPENROUTER", PlannerConventions.LlmProviders.OpenRouter);
    }

    [Fact]
    public void DefaultOpenRouterLlmModel_IsPinned()
    {
        Assert.Equal("deepseek/deepseek-v4-flash:free", PlannerConventions.DefaultOpenRouterLlmModel);
    }

    [Fact]
    public void Llm_ToolCallPrefix_IsPinned()
    {
        Assert.Equal("TOOL_CALL:", PlannerConventions.Llm.ToolCallPrefix);
    }

    [Fact]
    public void ToolNames_IndividualValues_ArePinned()
    {
        Assert.Equal("propose_plan", PlannerConventions.ToolNames.ProposePlan);
        Assert.Equal("get_allowed_namespaces", PlannerConventions.ToolNames.GetAllowedNamespaces);
        Assert.Equal("get_k8s_status", PlannerConventions.ToolNames.GetK8sStatus);
        Assert.Equal("get_k8s_events", PlannerConventions.ToolNames.GetK8sEvents);
        Assert.Equal("get_k8s_pods", PlannerConventions.ToolNames.GetK8sPods);
        Assert.Equal("describe_k8s_resource", PlannerConventions.ToolNames.DescribeK8sResource);
        Assert.Equal("get_k8s_deployments", PlannerConventions.ToolNames.GetK8sDeployments);
        Assert.Equal("get_k8s_services", PlannerConventions.ToolNames.GetK8sServices);
        Assert.Equal("get_k8s_endpoints", PlannerConventions.ToolNames.GetK8sEndpoints);
    }

    [Fact]
    public void ToolArguments_ArePinned()
    {
        Assert.Equal("operationType", PlannerConventions.ToolArguments.OperationType);
        Assert.Equal("arguments", PlannerConventions.ToolArguments.OperationArguments);
        Assert.Equal("name", PlannerConventions.ToolArguments.Name);
        Assert.Equal("namespace", PlannerConventions.ToolArguments.Namespace);
        Assert.Equal("replicas", PlannerConventions.ToolArguments.Replicas);
        Assert.Equal("container", PlannerConventions.ToolArguments.Container);
        Assert.Equal("image", PlannerConventions.ToolArguments.Image);
    }

    [Fact]
    public void FilterDropReasons_ArePinned()
    {
        Assert.Equal("resolved", PlannerConventions.FilterDropReasons.Resolved);
        Assert.Equal("unsupported_kind", PlannerConventions.FilterDropReasons.UnsupportedKind);
        Assert.Equal("dedupe:active_plan", PlannerConventions.FilterDropReasons.DedupeActivePlan);
        Assert.Equal("dedupe:operation_in_batch", PlannerConventions.FilterDropReasons.DedupeOperationInBatch);
    }

    [Fact]
    public void HttpClients_ExecutorHandoff_IsPinned()
    {
        Assert.Equal("ExecutorHandoff", PlannerConventions.HttpClients.ExecutorHandoff);
    }

    [Fact]
    public void Claims_AuthorizedParty_IsPinned()
    {
        Assert.Equal("azp", PlannerConventions.Claims.AuthorizedParty);
    }

    [Fact]
    public void ServiceClients_Observer_IsPinned()
    {
        Assert.Equal("infra-gate-observer", PlannerConventions.ServiceClients.Observer);
    }

    [Fact]
    public void Policies_ObserverSender_IsPinned()
    {
        Assert.Equal("ObserverSender", PlannerConventions.Policies.ObserverSender);
    }
}
