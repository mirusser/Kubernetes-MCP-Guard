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
    public void GeneratedFile_MarkerStrings_ArePinned()
    {
        Assert.Equal("# Generated from ", RunProfileConventions.GeneratedFile.HeaderLinePrefix);
        Assert.Equal(" profile: ", RunProfileConventions.GeneratedFile.ProfileMarker);
        Assert.StartsWith("# Do not edit.", RunProfileConventions.GeneratedFile.DoNotEditLinePrefix);
    }

    [Fact]
    public void DomainAdapterTypes_Kubernetes_IsPinned()
    {
        Assert.Equal("kubernetes", RunProfileConventions.DomainAdapterTypes.Kubernetes);
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
        Assert.Equal("InfraGate__Gateway__AspNetCoreUrls", RunProfileConventions.Env.AspnetcoreUrls);
        Assert.Equal("InfraGate__Observer__GatewayBaseUrl", RunProfileConventions.Env.ObserverGatewayBaseUrl);
        Assert.Equal("InfraGate__Planner__GatewayBaseUrl", RunProfileConventions.Env.PlannerGatewayBaseUrl);
        Assert.Equal("InfraGate__Executor__GatewayBaseUrl", RunProfileConventions.Env.ExecutorGatewayBaseUrl);
        Assert.Equal("InfraGate__Approval__Root", RunProfileConventions.Env.ApprovalRoot);
    }
}
