using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.BinaryIntegrity;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class KubernetesMcpServerProcessOptionsTests : IDisposable
{
    private const string Command = ".tools/bin/kubernetes-mcp-server";
    private const string OAuthAuthority = "https://issuer.example.com";
    private const string PrimaryKubeconfigConfigurationKey = "InfraGate:Kubernetes:KubeConfig";
    private const string SecondaryAllowedNamespacesKey = "AllowedNamespaces";
    private const string SecondaryContextKey = "Context";
    private const string SecondaryKubeconfigKey = "Kubeconfig";
    private const string SecondaryContext = "minikube-mcp";
    private const string SecondaryNamespace = "mcp-nginx-demo";

    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "infra-gate-k8s-mcp-options-tests",
        Guid.NewGuid().ToString("N"));

    private string PrimaryKubeconfig { get; }
    private string ViewerKubeconfig { get; }

    public KubernetesMcpServerProcessOptionsTests()
    {
        Directory.CreateDirectory(testRoot);
        PrimaryKubeconfig = WriteKubeconfig("primary.config", SecondaryContext);
        ViewerKubeconfig = WriteKubeconfig("viewer.config", SecondaryContext);
    }

    [Fact]
    public void FromConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => KubernetesMcpServerProcessOptions.FromConfiguration(null!));
    }

    [Fact]
    public void FromConfiguration_WithoutCommandConfigured_ReturnsNull()
    {
        var configuration = BuildConfig();

        KubernetesMcpServerProcessOptions? options = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.Null(options);
    }

    [Fact]
    public void FromConfiguration_WhitespaceOnlyCommand_ReturnsNull()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", "   "));

        KubernetesMcpServerProcessOptions? options = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.Null(options);
    }

    [Fact]
    public void FromConfiguration_MalformedArgumentsShape_ThrowsInvalidOperationException()
    {
        // "Arguments" configured as a scalar value rather than an indexed array
        // (Arguments:0, Arguments:1, ...) has no bindable children, so Get<string[]>()
        // returns null and the production code's `?? []` fallback applies.
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, ViewerKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments", "--config deploy/generated/k8s-mcp.toml"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains("--config", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_ArgumentsOverrideFixedPolicy_ThrowsInvalidOperationException()
    {
        var configuration = BuildEnabledConfig(
            ViewerKubeconfig,
            SecondaryContext,
            "--config",
            "deploy/generated/k8s-mcp.toml",
            "--read-only=false");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains("exactly", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromConfiguration_ConfigDirectoryContainsTomlDropIn_ThrowsInvalidOperationException()
    {
        string configDirectory = Path.Combine(testRoot, "config");
        string dropInDirectory = Path.Combine(configDirectory, "conf.d");
        Directory.CreateDirectory(dropInDirectory);
        string configPath = Path.Combine(configDirectory, "main.toml");
        File.WriteAllText(configPath, "read_only = true");
        File.WriteAllText(Path.Combine(dropInDirectory, "override.toml"), "read_only = false");
        var configuration = BuildEnabledConfig(
            ViewerKubeconfig,
            SecondaryContext,
            "--config",
            configPath);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains("drop-in", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromConfiguration_WithCommandAndArgumentsConfigured_ReturnsPopulatedOptions()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, ViewerKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:0", "--config"),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:1", "deploy/generated/k8s-mcp.toml"),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":WorkingDirectory", "/repo"));

        KubernetesMcpServerProcessOptions? options = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.NotNull(options);
        Assert.Equal(Command, options.Command);
        Assert.Equal(["--config", "deploy/generated/k8s-mcp.toml"], options.Arguments);
        Assert.Equal("/repo", options.WorkingDirectory);
        Assert.Equal(ViewerKubeconfig, options.Kubeconfig);
        Assert.Equal(SecondaryContext, options.Context);
        Assert.Equal([SecondaryNamespace], options.AllowedNamespaces);
    }

    [Fact]
    public void FromConfiguration_ArgumentsContainKubeconfigFlag_ThrowsInvalidOperationException()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, ViewerKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:0", "--kubeconfig"),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:1", PrimaryKubeconfig));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains("--config", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_EnabledWithoutViewerKubeconfig_ThrowsInvalidOperationException(
        string? kubeconfig)
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, kubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains(SecondaryKubeconfigKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_ViewerKubeconfigMatchesPrimary_ThrowsInvalidOperationException()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, PrimaryKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:0", "--config"),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:1", "deploy/generated/k8s-mcp.toml"),
            (PrimaryKubeconfigConfigurationKey, PrimaryKubeconfig));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains("distinct", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromConfiguration_PrimaryRelativePathAndSecondaryAbsolutePathMatch_ThrowsInvalidOperationException()
    {
        string secondaryWorkingDirectory = Path.Combine(Path.GetTempPath(), "infra-gate-secondary");
        string primaryRelativeKubeconfig = Path.GetRelativePath(Directory.GetCurrentDirectory(), PrimaryKubeconfig);
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey,
                PrimaryKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey,
                SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" +
                SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":WorkingDirectory",
                secondaryWorkingDirectory),
            (PrimaryKubeconfigConfigurationKey, primaryRelativeKubeconfig));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains("distinct", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromConfiguration_ViewerKubeconfigDiffersFromPrimary_ReturnsPopulatedOptions()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, ViewerKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:0", "--config"),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:1", "deploy/generated/k8s-mcp.toml"),
            (PrimaryKubeconfigConfigurationKey, PrimaryKubeconfig));

        KubernetesMcpServerProcessOptions? options =
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.NotNull(options);
        Assert.Equal(ViewerKubeconfig, options.Kubeconfig);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_EnabledWithoutContext_ThrowsInvalidOperationException(string? context)
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, ViewerKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, context),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains(SecondaryContextKey, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    public void FromConfiguration_EnabledWithoutExactNamespace_ThrowsInvalidOperationException(
        string? allowedNamespace)
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, ViewerKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", allowedNamespace));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains(SecondaryAllowedNamespacesKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_AllowedNamespacesContainsBlankEntry_ThrowsInvalidOperationException()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, ViewerKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":1", "   "));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains(SecondaryAllowedNamespacesKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_WithoutWorkingDirectoryConfigured_DefaultsToCurrentDirectory()
    {
        var configuration = BuildConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, ViewerKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:0", "--config"),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:1", "deploy/generated/k8s-mcp.toml"));

        KubernetesMcpServerProcessOptions? options = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.NotNull(options);
        Assert.Equal(Directory.GetCurrentDirectory(), options.WorkingDirectory);
    }

    [Fact]
    public void FromConfiguration_ViewerKubeconfigDoesNotExist_ThrowsInvalidOperationException()
    {
        var configuration = BuildEnabledConfig(Path.Combine(testRoot, "missing.config"), SecondaryContext);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains("does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromConfiguration_ViewerKubeconfigCurrentContextDiffers_ThrowsInvalidOperationException()
    {
        string kubeconfig = WriteKubeconfig("mismatched.config", "other-context");
        var configuration = BuildEnabledConfig(kubeconfig, SecondaryContext);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains("current context", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromConfiguration_ViewerKubeconfigContainsMultipleContexts_ThrowsInvalidOperationException()
    {
        string kubeconfig = WriteKubeconfig("multiple.config", SecondaryContext, "other-context");
        var configuration = BuildEnabledConfig(kubeconfig, SecondaryContext);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            KubernetesMcpServerProcessOptions.FromConfiguration(configuration));

        Assert.Contains("exactly one context", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthRequired_IsAlwaysFalse()
    {
        Assert.False(KubernetesMcpServerProcessOptions.AuthRequired);
    }

    [Fact]
    public void ValidateProductionSafety_UnaffectedByKubernetesMcpServerOptionsPresence()
    {
        var configuration = BuildProductionConfig(
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey, ViewerKubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey, SecondaryContext),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:0", "--config"),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Arguments:1", "deploy/generated/k8s-mcp.toml"));

        var options = McpGatewayOptions.FromConfiguration(configuration);
        KubernetesMcpServerProcessOptions? secondary = KubernetesMcpServerProcessOptions.FromConfiguration(configuration);

        Assert.NotNull(secondary);
        Exception? exception = Record.Exception(() => options.ValidateProductionSafety(
            new FakeVerifier(options.DownstreamAssembly, options.DownstreamAssemblyHash)));

        Assert.Null(exception);
    }

    private sealed class FakeVerifier(string? assembly, string? hash) : IDownstreamBinaryIntegrityVerifier
    {
        public void Verify(string downstreamAssembly, string expectedHash)
        {
            Assert.Equal(assembly, downstreamAssembly);
            Assert.Equal(hash, expectedHash);
        }
    }

    private static IConfiguration BuildConfig(params (string Key, string? Value)[] entries)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => e.Value))
            .Build();
    }

    private static IConfiguration BuildEnabledConfig(
        string kubeconfig,
        string context,
        params string[] arguments)
    {
        if (arguments.Length == 0)
        {
            arguments = ["--config", "deploy/generated/k8s-mcp.toml"];
        }

        var entries = new List<(string Key, string? Value)>
        {
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":Command", Command),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryKubeconfigKey,
                kubeconfig),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" + SecondaryContextKey,
                context),
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + ":" +
                SecondaryAllowedNamespacesKey + ":0", SecondaryNamespace)
        };
        entries.AddRange(arguments.Select((argument, index) =>
            (McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection + $":Arguments:{index}",
                (string?)argument)));

        return BuildConfig([.. entries]);
    }

    private string WriteKubeconfig(string fileName, params string[] contexts)
    {
        string path = Path.Combine(testRoot, fileName);
        string contextEntries = string.Join(
            Environment.NewLine,
            contexts.Select(context =>
                $"- name: {context}\n  context:\n    cluster: demo\n    user: viewer"));
        string content =
            "apiVersion: v1\n" +
            "kind: Config\n" +
            "clusters:\n" +
            "- name: demo\n" +
            "  cluster:\n" +
            "    server: https://127.0.0.1\n" +
            "contexts:\n" +
            contextEntries + "\n" +
            $"current-context: {contexts[0]}\n" +
            "users:\n" +
            "- name: viewer\n" +
            "  user:\n" +
            "    token: test-token\n";
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        Directory.Delete(testRoot, recursive: true);
    }

    private static IConfiguration BuildProductionConfig(params (string Key, string? Value)[] overrides)
    {
        var entries = new Dictionary<string, string?>
        {
            [RuntimeSafetyConventions.ConfigurationKeys.InfraGateRuntimeEnvironment] =
                RuntimeSafetyConventions.EnvironmentValues.Production,
            [GatewayAuthConventions.ConfigurationKeys.OAuthAuthority] = OAuthAuthority,
            [GatewayAuthConventions.ConfigurationKeys.OAuthResource] = "https://gateway.example.com/mcp",
            [GatewayAuthConventions.ConfigurationKeys.OAuthScope] = GatewayAuthConventions.DefaultOAuthScope,
            [GatewayAuthConventions.ConfigurationKeys.OAuthRequireHttpsMetadata] = "true",
            [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthAuthorizationEndpoint] = OAuthAuthority + "/authorize",
            [GatewayAuthConventions.ConfigurationKeys.ApprovalOAuthTokenEndpoint] = OAuthAuthority + "/token",
            [GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionEnabled] = "true",
            [GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionClientId] = "gateway-resource-server",
            [GatewayAuthConventions.ConfigurationKeys.TokenIntrospectionClientSecret] = "secret-placeholder",
            [GatewayAuthConventions.ConfigurationKeys.MaxAcceptedAccessTokenLifetimeSeconds] = "300",
            [McpGatewayConventions.ConfigurationKeys.ApprovalBaseUrl] = "https://gateway.example.com",
            [McpGatewayConventions.ConfigurationKeys.GuardAuditRoot] = "/var/lib/infra-gate/production/guardrails",
            [McpGatewayConventions.ConfigurationKeys.ApprovalRoot] = "/var/lib/infra-gate/production/approvals",
            [McpGatewayConventions.ConfigurationKeys.DownstreamAssembly] = "/app/server/InfraGate.McpServer.dll",
            [McpGatewayConventions.ConfigurationKeys.DownstreamAssemblyHash] =
                "a3e5f8c9d2b1e4076f5a3c8e1d0b9a2c7f4e6d5b8c3a1f0e9d7b6c5a4f3e2d1b0",
            [DownstreamAuthConventions.ConfigurationKeys.Required] = "true",
            [DownstreamAuthConventions.ConfigurationKeys.Authority] = "https://idp.example.com",
            [DownstreamAuthConventions.ConfigurationKeys.RequireHttpsMetadata] = "true",
            [DownstreamAuthConventions.ConfigurationKeys.GatewayClientId] = "infra-gate-gateway",
        };

        foreach ((string key, string? value) in overrides)
        {
            entries[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(entries).Build();
    }
}
