namespace InfraGate.RunProfiles.Tests.UnitTests;

public sealed class TomlFileRendererTests
{
    [Fact]
    public void Render_EmptyConfigFileName_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            TomlFileRenderer.Render("", CreateProfileWithKubernetesAdapter()));

        Assert.Contains("configFileName", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_NullProfile_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            TomlFileRenderer.Render("run-profiles.yaml", null!));

        Assert.Equal("profile", ex.ParamName);
    }

    [Fact]
    public void Render_NoKubernetesMcpServerProfile_ThrowsInvalidOperationException()
    {
        var profile = CreateMinimalProfile();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TomlFileRenderer.Render("run-profiles.yaml", profile));

        Assert.Contains("Kubernetes MCP Server", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithKubernetesMcpServerProfile_EmitsFixedSingleClusterPolicy()
    {
        var profile = CreateProfileWithKubernetesAdapter();

        string result = TomlFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("kubeconfig = \".kube/mcp-nginx-demo-viewer.config\"", result, StringComparison.Ordinal);
        Assert.Contains("cluster_provider_strategy = \"disabled\"", result, StringComparison.Ordinal);
        Assert.Contains("cluster_auth_mode = \"kubeconfig\"", result, StringComparison.Ordinal);
        Assert.Contains("toolsets = [\"core\"]", result, StringComparison.Ordinal);
        Assert.Contains("stateless = true", result, StringComparison.Ordinal);
        Assert.Contains("read_only = true", result, StringComparison.Ordinal);
        Assert.Contains("disable_destructive = true", result, StringComparison.Ordinal);
        Assert.Contains(
            "enabled_tools = [\"pods_list_in_namespace\", \"pods_get\", \"pods_log\"]",
            result,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"pods_list\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("events_list", result, StringComparison.Ordinal);
        Assert.DoesNotContain("resources_get", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ReadOnlyIsNotDisableable()
    {
        var profile = CreateProfileWithKubernetesAdapter();

        string result = TomlFileRenderer.Render("run-profiles.yaml", profile);

        Assert.Contains("read_only = true", result, StringComparison.Ordinal);
        Assert.DoesNotContain("read_only = false", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_EnabledToolsMatchesSharedCuratedList()
    {
        var profile = CreateProfileWithKubernetesAdapter();

        string result = TomlFileRenderer.Render("run-profiles.yaml", profile);

        foreach (string tool in KubernetesMcpServerProfile.EnabledTools)
        {
            Assert.Contains($"\"{tool}\"", result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Render_KubeconfigContainsBackslash_EscapesBackslashForValidToml()
    {
        // Raw value: C:\configs\demo.config
        var profile = CreateProfileWithKubernetesAdapter(kubeconfig: @"C:\configs\demo.config");

        string result = TomlFileRenderer.Render("run-profiles.yaml", profile);

        // Each single backslash in the raw value must become two backslashes in the TOML output.
        Assert.Contains(
            "kubeconfig = \"C:\\\\configs\\\\demo.config\"",
            result,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Render_KubeconfigContainsDoubleQuote_EscapesQuoteForValidToml()
    {
        // Raw value: .kube/weird"name.config
        var profile = CreateProfileWithKubernetesAdapter(kubeconfig: ".kube/weird\"name.config");

        string result = TomlFileRenderer.Render("run-profiles.yaml", profile);

        // The embedded double-quote must be escaped so the TOML string terminates correctly.
        Assert.Contains(
            "kubeconfig = \".kube/weird\\\"name.config\"",
            result,
            StringComparison.Ordinal);
    }

    private static RunProfile CreateMinimalProfile() =>
        new("test-profile", "mcp-stdio", null, null, null, null, null, [], null, null, null, null, null, null);

    private static RunProfile CreateProfileWithKubernetesAdapter(
        string kubeconfig = ".kube/mcp-nginx-demo-viewer.config") =>
        CreateMinimalProfile() with
        {
            DomainAdapters =
            [
                new DomainAdapterProfile(
                    "kubernetesAdapter",
                    RunProfileConventions.DomainAdapterTypes.Kubernetes,
                    new KubernetesAdapterProfile(kubeconfig, [ "mcp-nginx-demo" ])),
            ],
            KubernetesMcpServer = new KubernetesMcpServerProfile(
                kubeconfig,
                "minikube-mcp",
                ["mcp-nginx-demo"]),
        };
}
