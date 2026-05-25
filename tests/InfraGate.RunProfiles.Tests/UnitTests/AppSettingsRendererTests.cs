using System.Text.Json;
using InfraGate.RunProfiles;

namespace InfraGate.RunProfiles.Tests.UnitTests;

public sealed class AppSettingsRendererTests
{
    [Fact]
    public void Render_EmptyConfigFileName_ThrowsArgumentException()
    {
        var profile = CreateMinimalProfile();

        var ex = Assert.Throws<ArgumentException>(() => AppSettingsRenderer.Render("", profile));

        Assert.Contains("configFileName", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_NullProfile_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => AppSettingsRenderer.Render("config.yaml", null!));

        Assert.Equal("profile", ex.ParamName);
    }

    [Fact]
    public void Render_BooleanParseFailure_ThrowsInvalidOperationException()
    {
        var profile = CreateMinimalProfile() with
        {
            IdentityProvider = new IdentityProviderProfile(null, null, null, null, null, "not-a-bool")
        };

        var ex = Assert.Throws<InvalidOperationException>(() => AppSettingsRenderer.Render("config.yaml", profile));

        Assert.Contains("must be 'true' or 'false'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("not-a-bool", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_NoRuntimeMode_OmitsRuntimeSection()
    {
        var profile = CreateMinimalProfile();

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate;
        Assert.True(doc.RootElement.TryGetProperty("InfraGate", out infraGate));
        Assert.False(infraGate.TryGetProperty("Runtime", out _));
    }

    [Fact]
    public void Render_NullGateway_OmitsGatewaySection()
    {
        var profile = CreateMinimalProfile();

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        Assert.False(infraGate.TryGetProperty("Gateway", out _));
    }

    [Fact]
    public void Render_NullIdentityProviderAndNullApprovalAuthority_OmitsAuthSection()
    {
        var profile = CreateMinimalProfile() with
        {
            IdentityProvider = null,
            ApprovalAuthority = null
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        Assert.False(infraGate.TryGetProperty("Auth", out _));
    }

    [Fact]
    public void Render_NoKubernetesDomainAdapter_OmitsKubernetesSection()
    {
        var profile = new RunProfile(
            "test",
            "mcp-stdio",
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            null,
            null,
            null,
            null);

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        Assert.False(infraGate.TryGetProperty("Kubernetes", out _));
    }

    [Fact]
    public void Render_DomainAdapterWithoutKubernetes_OmitsKubernetesSection()
    {
        var profile = new RunProfile(
            "test",
            "mcp-stdio",
            null,
            null,
            null,
            null,
            null,
            [new DomainAdapterProfile("k8s", "kubernetes", null)],
            null,
            null,
            null,
            null,
            null);

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        Assert.False(infraGate.TryGetProperty("Kubernetes", out _));
    }

    [Fact]
    public void Render_NoApprovalRootOrBaseUrl_OmitsApprovalSection()
    {
        var profile = CreateMinimalProfile();

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        Assert.False(infraGate.TryGetProperty("Approval", out _));
    }

    [Fact]
    public void Render_ApprovalRootPresent_IncludesApprovalSectionWithRoot()
    {
        var profile = CreateMinimalProfile() with
        {
            GenericApprovalCore = new GenericApprovalCoreProfile("/data/approvals")
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        JsonElement approval = infraGate.GetProperty("Approval");
        Assert.Equal("/data/approvals", approval.GetProperty("Root").GetString());
    }

    [Fact]
    public void Render_ApprovalBaseUrlPresent_IncludesApprovalSectionWithBaseUrl()
    {
        var profile = CreateMinimalProfile() with
        {
            ApprovalAuthority = new ApprovalAuthorityProfile("http://gateway.test", null, null, null, null)
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        JsonElement approval = infraGate.GetProperty("Approval");
        Assert.Equal("http://gateway.test", approval.GetProperty("BaseUrl").GetString());
    }

    [Fact]
    public void Render_EmptyGatewayValues_OmitsGatewaySection()
    {
        var profile = CreateMinimalProfile() with
        {
            Gateway = new GatewayProfile(null, null, null)
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        Assert.False(infraGate.TryGetProperty("Gateway", out _));
    }

    [Fact]
    public void Render_EmptyAuthValues_OmitsAuthSection()
    {
        var profile = CreateMinimalProfile() with
        {
            IdentityProvider = new IdentityProviderProfile(null, null, null, null, null, null),
            ApprovalAuthority = new ApprovalAuthorityProfile(null, null, null, null, null)
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        Assert.False(infraGate.TryGetProperty("Auth", out _));
    }

    [Fact]
    public void Render_PostgresConnectionStringPresent_WritesNestedPostgresObject()
    {
        var profile = CreateMinimalProfile() with
        {
            GenericApprovalCore = new GenericApprovalCoreProfile("/data/approvals", "Host=postgres;Port=5432;Database=approvals;Username=app;Password=secret")
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement approval = doc.RootElement.GetProperty("InfraGate").GetProperty("Approval");
        JsonElement postgres = approval.GetProperty("Postgres");
        Assert.Equal("Host=postgres;Port=5432;Database=approvals;Username=app;Password=secret", postgres.GetProperty("ConnectionString").GetString());
    }

    [Fact]
    public void Render_PostgresConnectionStringNull_OmitsPostgresSection()
    {
        var profile = CreateMinimalProfile() with
        {
            GenericApprovalCore = new GenericApprovalCoreProfile("/data/approvals")
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement approval = doc.RootElement.GetProperty("InfraGate").GetProperty("Approval");
        Assert.False(approval.TryGetProperty("Postgres", out _));
    }

    [Fact]
    public void Render_MultipleAllowedNamespaces_WritesArray()
    {
        var profile = CreateMinimalProfile() with
        {
            DomainAdapters =
            [
                new DomainAdapterProfile("k8s", "kubernetes",
                    new KubernetesAdapterProfile("/run/kube/config", ["ns1", "ns2", "ns3"]))
            ]
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement infraGate = doc.RootElement.GetProperty("InfraGate");
        JsonElement k8s = infraGate.GetProperty("Kubernetes");
        Assert.Equal(3, k8s.GetProperty("AllowedNamespaces").GetArrayLength());
    }

    [Fact]
    public void Render_ObserverCadencePresent_WritesCycleIntervalSeconds()
    {
        var profile = CreateMinimalProfile() with
        {
            Observer = new ObserverProfile(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "60",
                null,
                null,
                null,
                null)
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement observer = doc.RootElement.GetProperty("InfraGate").GetProperty("Observer");
        Assert.Equal("60", observer.GetProperty("CycleIntervalSeconds").GetString());
        Assert.False(observer.TryGetProperty("CycleCadenceSeconds", out _));
    }

    [Fact]
    public void Render_PlannerGatewayBaseUrlPresent_WritesPlannerSection()
    {
        var profile = CreateMinimalProfile() with
        {
            Planner = new PlannerProfile(
                null,
                "http://gateway:3001/mcp",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null)
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement planner = doc.RootElement.GetProperty("InfraGate").GetProperty("Planner");
        Assert.Equal("http://gateway:3001/mcp", planner.GetProperty("GatewayBaseUrl").GetString());
    }

    [Fact]
    public void Render_ExecutorConcurrencyCapPresent_WritesExecutorSection()
    {
        var profile = CreateMinimalProfile() with
        {
            Executor = new ExecutorProfile(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "64",
                null,
                null)
        };

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        JsonElement executor = doc.RootElement.GetProperty("InfraGate").GetProperty("Executor");
        Assert.Equal("64", executor.GetProperty("ConcurrencyCap").GetString());
    }

    [Fact]
    public void Render_NullPlanner_OmitsPlannerSection()
    {
        var profile = CreateMinimalProfile();

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("InfraGate").TryGetProperty("Planner", out _));
    }

    [Fact]
    public void Render_NullExecutor_OmitsExecutorSection()
    {
        var profile = CreateMinimalProfile();

        string result = AppSettingsRenderer.Render("config.yaml", profile);

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("InfraGate").TryGetProperty("Executor", out _));
    }

    private static RunProfile CreateMinimalProfile() =>

        new(
            "test",
            "mcp-stdio",
            null,
            null,
            null,
            null,
            null,
            [new DomainAdapterProfile("k8s", "kubernetes",
                new KubernetesAdapterProfile("/run/kube/config", ["default"]))],
            null,
            null,
            null,
            null,
            null);
}
