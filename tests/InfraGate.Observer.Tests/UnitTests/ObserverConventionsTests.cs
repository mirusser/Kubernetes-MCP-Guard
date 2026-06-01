namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ObserverConventionsTests
{
    [Fact]
    public void DefaultUrl_UsesPort3003()
    {
        Assert.EndsWith(":3003", ObserverConventions.DefaultUrl);
    }

    [Fact]
    public void DefaultUrl_UsesLoopbackAddress()
    {
        Assert.StartsWith("http://127.0.0.1", ObserverConventions.DefaultUrl);
    }

    [Fact]
    public void HealthEndpointPath_IsPinned()
    {
        Assert.Equal("/health", ObserverConventions.HealthEndpointPath);
    }

    [Fact]
    public void ObserveNowEndpointPath_IsPinned()
    {
        Assert.Equal("/observe-now", ObserverConventions.ObserveNowEndpointPath);
    }

    [Fact]
    public void ObserveNowTimeoutSeconds_Is30()
    {
        Assert.Equal(30, ObserverConventions.ObserveNowTimeoutSeconds);
    }

    [Fact]
    public void OnDemandSlackWindowSeconds_Is2()
    {
        Assert.Equal(2, ObserverConventions.OnDemandSlackWindowSeconds);
    }

    [Fact]
    public void DefaultClientId_IsPinned()
    {
        Assert.Equal("infra-gate-observer", ObserverConventions.DefaultClientId);
    }

    [Fact]
    public void DefaultOAuthScope_IsPinned()
    {
        Assert.Equal("mcp:tools.readonly", ObserverConventions.DefaultOAuthScope);
    }

    [Fact]
    public void ConfigurationKeys_ArePinned()
    {
        Assert.Equal("InfraGate:Observer", ObserverConventions.ConfigurationKeys.Observer);
        Assert.Equal("InfraGate:Observer:CycleIntervalSeconds", ObserverConventions.ConfigurationKeys.CycleIntervalSeconds);
        Assert.Equal("InfraGate:Observer:GatewayBaseUrl", ObserverConventions.ConfigurationKeys.GatewayBaseUrl);
        Assert.Equal("InfraGate:Observer:AllowedNamespaces", ObserverConventions.ConfigurationKeys.AllowedNamespaces);
        Assert.Equal("InfraGate:Observer:LlmProvider", ObserverConventions.ConfigurationKeys.LlmProvider);
        Assert.Equal("InfraGate:Observer:LlmModel", ObserverConventions.ConfigurationKeys.LlmModel);
        Assert.Equal("InfraGate:Observer:FileSink:Root", ObserverConventions.ConfigurationKeys.FileSinkRoot);
        Assert.Equal("InfraGate:Observer:PlannerHandoffUrl", ObserverConventions.ConfigurationKeys.PlannerHandoffUrl);
    }

    [Fact]
    public void EnvironmentVariables_ArePinned()
    {
        Assert.Equal("ASPNETCORE_URLS", ObserverConventions.EnvironmentVariables.AspNetCoreUrls);
        Assert.Equal("INFRA_GATE_OBSERVER_GATEWAY_BASE_URL", ObserverConventions.EnvironmentVariables.GatewayBaseUrl);
        Assert.Equal("INFRA_GATE_OBSERVER_ALLOWED_NAMESPACES", ObserverConventions.EnvironmentVariables.AllowedNamespaces);
        Assert.Equal("INFRA_GATE_OBSERVER_LLM_PROVIDER", ObserverConventions.EnvironmentVariables.LlmProvider);
        Assert.Equal("INFRA_GATE_OBSERVER_LLM_MODEL", ObserverConventions.EnvironmentVariables.LlmModel);
        Assert.Equal("INFRA_GATE_OBSERVER_LLM_API_KEY", ObserverConventions.EnvironmentVariables.LlmApiKey);
        Assert.Equal("INFRA_GATE_OBSERVER_CLIENT_ID", ObserverConventions.EnvironmentVariables.ClientId);
        Assert.Equal("INFRA_GATE_OBSERVER_CLIENT_SECRET", ObserverConventions.EnvironmentVariables.ClientSecret);
        Assert.Equal("INFRA_GATE_OBSERVER_OAUTH_AUTHORITY", ObserverConventions.EnvironmentVariables.OAuthAuthority);
        Assert.Equal("INFRA_GATE_OBSERVER_OAUTH_SCOPE", ObserverConventions.EnvironmentVariables.OAuthScope);
        Assert.Equal("INFRA_GATE_OBSERVER_PLANNER_HANDOFF_URL", ObserverConventions.EnvironmentVariables.PlannerHandoffUrl);
        Assert.Equal("INFRA_GATE_OBSERVER_FILE_SINK_ROOT", ObserverConventions.EnvironmentVariables.FileSinkRoot);
    }

    [Fact]
    public void LlmProviders_ArePinned()
    {
        Assert.Equal("ANTHROPIC", ObserverConventions.LlmProviders.Anthropic);
        Assert.Equal("OPENAI", ObserverConventions.LlmProviders.OpenAI);
        Assert.Equal("GOOGLE", ObserverConventions.LlmProviders.Google);
        Assert.Equal("AZURE", ObserverConventions.LlmProviders.Azure);
        Assert.Equal("OLLAMA", ObserverConventions.LlmProviders.Ollama);
        Assert.Equal("OPENROUTER", ObserverConventions.LlmProviders.OpenRouter);
    }

    [Fact]
    public void HttpClients_PlannerHandoff_IsPinned()
    {
        Assert.Equal("PlannerHandoff", ObserverConventions.HttpClients.PlannerHandoff);
    }

    [Fact]
    public void ToolNames_IndividualValues_ArePinned()
    {
        Assert.Equal("get_allowed_namespaces", ObserverConventions.ToolNames.GetAllowedNamespaces);
        Assert.Equal("get_k8s_status", ObserverConventions.ToolNames.GetK8sStatus);
        Assert.Equal("get_k8s_events", ObserverConventions.ToolNames.GetK8sEvents);
        Assert.Equal("get_pod_logs", ObserverConventions.ToolNames.GetPodLogs);
        Assert.Equal("get_k8s_resource", ObserverConventions.ToolNames.GetK8sResource);
        Assert.Equal("get_deployment_diagnostics", ObserverConventions.ToolNames.GetDeploymentDiagnostics);
        Assert.Equal("get_pod_diagnostics", ObserverConventions.ToolNames.GetPodDiagnostics);
        Assert.Equal("get_service_diagnostics", ObserverConventions.ToolNames.GetServiceDiagnostics);
    }

}
