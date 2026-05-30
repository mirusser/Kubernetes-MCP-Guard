namespace InfraGate.RunProfiles.Tests.UnitTests;

public sealed class RunProfileConventionsTests
{
    [Fact]
    public void DefaultConfigPath_IsPinned()
    {
        Assert.Equal("deploy/run-profiles.yaml", RunProfileConventions.DefaultConfigPath);
    }

    [Fact]
    public void Commands_ArePinned()
    {
        Assert.Equal("generate", RunProfileConventions.Commands.Generate);
        Assert.Equal("list", RunProfileConventions.Commands.List);
        Assert.Equal("validate", RunProfileConventions.Commands.Validate);
    }

    [Fact]
    public void Options_ArePinned()
    {
        Assert.Equal("--config", RunProfileConventions.Options.Config);
        Assert.Equal("--format", RunProfileConventions.Options.Format);
        Assert.Equal("--force", RunProfileConventions.Options.Force);
        Assert.Equal("--output", RunProfileConventions.Options.Output);
        Assert.Equal("--set", RunProfileConventions.Options.Set);
    }

    [Fact]
    public void Formats_ArePinned()
    {
        Assert.Equal("appsettings", RunProfileConventions.Formats.AppSettingJson);
        Assert.Equal("env", RunProfileConventions.Formats.DotEnv);
    }

    [Fact]
    public void GeneratedFile_MarkerStrings_ArePinned()
    {
        Assert.Equal("# Generated from ", RunProfileConventions.GeneratedFile.HeaderLinePrefix);
        Assert.Equal(" profile: ", RunProfileConventions.GeneratedFile.ProfileMarker);
        Assert.StartsWith("# Do not edit.", RunProfileConventions.GeneratedFile.DoNotEditLinePrefix);
        Assert.Equal("_generated", RunProfileConventions.GeneratedFile.MetadataSection);
        Assert.Equal("profile", RunProfileConventions.GeneratedFile.MetadataProfile);
        Assert.Equal("source", RunProfileConventions.GeneratedFile.MetadataSource);
    }

    [Fact]
    public void DomainAdapterTypes_Kubernetes_IsPinned()
    {
        Assert.Equal("kubernetes", RunProfileConventions.DomainAdapterTypes.Kubernetes);
    }

    [Fact]
    public void RuntimeConfig_ContainerPath_IsPinned()
    {
        Assert.Equal("/app/config/appsettings.InfraGate.json", RunProfileConventions.RuntimeConfig.ContainerPath);
    }

    [Fact]
    public void AppSettings_CoreSectionNames_ArePinned()
    {
        Assert.Equal("InfraGate", RunProfileConventions.AppSettings.Root);
        Assert.Equal("Gateway", RunProfileConventions.AppSettings.Gateway);
        Assert.Equal("Observer", RunProfileConventions.AppSettings.Observer);
        Assert.Equal("Planner", RunProfileConventions.AppSettings.Planner);
        Assert.Equal("Executor", RunProfileConventions.AppSettings.Executor);
        Assert.Equal("Auth", RunProfileConventions.AppSettings.Auth);
        Assert.Equal("Approval", RunProfileConventions.AppSettings.Approval);
        Assert.Equal("Kubernetes", RunProfileConventions.AppSettings.Kubernetes);
    }

    [Fact]
    public void YamlKeys_CoreKeys_ArePinned()
    {
        Assert.Equal("profiles", RunProfileConventions.YamlKeys.Profiles);
        Assert.Equal("kind", RunProfileConventions.YamlKeys.Kind);
        Assert.Equal("name", RunProfileConventions.YamlKeys.Name);
        Assert.Equal("defaults", RunProfileConventions.YamlKeys.Defaults);
        Assert.Equal("domainAdapters", RunProfileConventions.YamlKeys.DomainAdapters);
        Assert.Equal("gateway", RunProfileConventions.YamlKeys.Gateway);
        Assert.Equal("observer", RunProfileConventions.YamlKeys.Observer);
        Assert.Equal("planner", RunProfileConventions.YamlKeys.Planner);
        Assert.Equal("executor", RunProfileConventions.YamlKeys.Executor);
        Assert.Equal("kubernetes", RunProfileConventions.YamlKeys.Kubernetes);
    }

    [Fact]
    public void Env_CoreVariables_ArePinned()
    {
        Assert.Equal("ASPNETCORE_URLS", RunProfileConventions.Env.AspnetcoreUrls);
        Assert.Equal("INFRA_GATE_OBSERVER_GATEWAY_BASE_URL", RunProfileConventions.Env.ObserverGatewayBaseUrl);
        Assert.Equal("INFRA_GATE_PLANNER_GATEWAY_BASE_URL", RunProfileConventions.Env.PlannerGatewayBaseUrl);
        Assert.Equal("INFRA_GATE_EXECUTOR_GATEWAY_BASE_URL", RunProfileConventions.Env.ExecutorGatewayBaseUrl);
        Assert.Equal("K8S_MCP_APPROVAL_ROOT", RunProfileConventions.Env.ApprovalRoot);
    }
}
