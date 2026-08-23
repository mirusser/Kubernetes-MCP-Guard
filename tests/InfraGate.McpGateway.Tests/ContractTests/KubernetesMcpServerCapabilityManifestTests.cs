using System.Security.Cryptography;
using System.Text.Json;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway.DownstreamAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.ContractTests;

public sealed class KubernetesMcpServerCapabilityManifestTests
{
    private const string RequireLiveContractEnvironmentVariable =
        "INFRA_GATE_REQUIRE_KUBERNETES_MCP_CAPABILITY_CONTRACT";

    [Fact]
    public void V0066_ClassifiesEveryReleasedTool()
    {
        KubernetesMcpServerCapabilityManifest manifest = KubernetesMcpServerCapabilityManifest.V0066;

        Assert.Equal("v0.0.66", manifest.Version);
        Assert.Equal(52, manifest.Tools.Count);
        Assert.Equal(52, manifest.Tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            Enum.GetValues<KubernetesMcpServerProcessRole>().Order(),
            manifest.Tools.Select(tool => tool.AllowedProcessRole).Distinct().Order());
        Assert.All(manifest.Tools, tool =>
        {
            Assert.NotEmpty(tool.Name);
            Assert.NotEmpty(tool.Toolset);
            Assert.Matches("^[0-9a-f]{64}$", tool.InputSchemaSha256);
            Assert.Matches("^[0-9a-f]{64}$", tool.AnnotationsSha256);
            Assert.NotEmpty(tool.IntentCodec);
            Assert.NotEmpty(tool.EvidenceStrategy);
            Assert.True(tool.MaximumOutputBytes >= 0);
        });
    }

    [Fact]
    public void V0066_RecordsDisableDestructiveAsInsufficientForWriteDenial()
    {
        KubernetesMcpServerCapabilityManifest manifest = KubernetesMcpServerCapabilityManifest.V0066;

        string[] additiveWrites = manifest.Tools
            .Where(tool => tool.IsReadOnly is false && tool.IsDestructive is false)
            .Select(tool => tool.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("helm_install", additiveWrites);
        Assert.Contains("pods_run", additiveWrites);
        Assert.Contains("tekton_pipeline_start", additiveWrites);
        Assert.Contains("tekton_task_start", additiveWrites);
        Assert.Contains("tekton_taskrun_restart", additiveWrites);
    }

    [Fact]
    public void TryValidateTool_ExactPinnedTool_Succeeds()
    {
        DownstreamTool tool = CreatePinnedPodsGetTool();

        bool valid = KubernetesMcpServerCapabilityManifest.V0066.TryValidateTool(
            tool,
            KubernetesMcpServerProcessRole.PublicViewer,
            out string error);

        Assert.True(valid, error);
        Assert.Empty(error);
    }

    [Fact]
    public void TryValidateTool_UnknownName_FailsClosed()
    {
        DownstreamTool tool = CreatePinnedPodsGetTool() with { Name = "pods_get_v2" };

        bool valid = KubernetesMcpServerCapabilityManifest.V0066.TryValidateTool(
            tool,
            KubernetesMcpServerProcessRole.PublicViewer,
            out string error);

        Assert.False(valid);
        Assert.Contains("not classified", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateTool_SchemaDrift_FailsClosed()
    {
        DownstreamTool tool = CreatePinnedPodsGetTool() with
        {
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Name of the Pod" },
                    @namespace = new { type = "string", description = "Namespace to get the Pod from" },
                    context = new { type = "string" }
                },
                required = new[] { "name" }
            })
        };

        bool valid = KubernetesMcpServerCapabilityManifest.V0066.TryValidateTool(
            tool,
            KubernetesMcpServerProcessRole.PublicViewer,
            out string error);

        Assert.False(valid);
        Assert.Contains("schema", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidateTool_AnnotationDrift_FailsClosed()
    {
        DownstreamTool tool = CreatePinnedPodsGetTool() with
        {
            Annotations = JsonSerializer.SerializeToElement(new
            {
                destructiveHint = false,
                idempotentHint = false,
                openWorldHint = true,
                readOnlyHint = false,
                title = "Pods: Get"
            })
        };

        bool valid = KubernetesMcpServerCapabilityManifest.V0066.TryValidateTool(
            tool,
            KubernetesMcpServerProcessRole.PublicViewer,
            out string error);

        Assert.False(valid);
        Assert.Contains("annotation", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidateTool_WrongProcessRole_FailsClosed()
    {
        DownstreamTool tool = CreatePinnedPodsGetTool();

        bool valid = KubernetesMcpServerCapabilityManifest.V0066.TryValidateTool(
            tool,
            KubernetesMcpServerProcessRole.HiddenExecutor,
            out string error);

        Assert.False(valid);
        Assert.Contains("role", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishSnapshot_CapabilityContractDrift_RejectsSnapshotAtomically()
    {
        var catalog = new DownstreamToolCatalog();
        DownstreamTool validTool = CreatePinnedPodsGetTool();
        ToolCatalogSnapshot first = await catalog.PublishCapabilitySnapshotAsync(
            McpGatewayConventions.DownstreamSources.Secondary,
            [validTool],
            new HashSet<string>(StringComparer.Ordinal) { validTool.Name },
            requestPolicy: null,
            responsePolicy: null,
            KubernetesMcpServerCapabilityManifest.V0066,
            KubernetesMcpServerProcessRole.PublicViewer,
            CancellationToken.None);
        Assert.True(first.IsValid, first.DegradedReason);

        DownstreamTool driftedTool = validTool with
        {
            Annotations = JsonSerializer.SerializeToElement(new
            {
                destructiveHint = false,
                idempotentHint = true,
                openWorldHint = true,
                readOnlyHint = true,
                title = "Pods: Get"
            })
        };

        ToolCatalogSnapshot rejected = await catalog.PublishCapabilitySnapshotAsync(
            McpGatewayConventions.DownstreamSources.Secondary,
            [driftedTool],
            new HashSet<string>(StringComparer.Ordinal) { driftedTool.Name },
            requestPolicy: null,
            responsePolicy: null,
            KubernetesMcpServerCapabilityManifest.V0066,
            KubernetesMcpServerProcessRole.PublicViewer,
            CancellationToken.None);

        Assert.False(rejected.IsValid);
        Assert.Contains("annotation", rejected.DegradedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, catalog.GetSourceGeneration(McpGatewayConventions.DownstreamSources.Secondary));
        Assert.Same(validTool, catalog.GetCatalogEntry("pods_get")?.Tool);
    }

    [Fact]
    public void PinnedArtifactMetadata_MatchesInstallerManifest()
    {
        string repoRoot = FindRepoRoot();
        using JsonDocument installerManifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "scripts", "kubernetes-mcp-server.manifest.json")));
        KubernetesMcpServerCapabilityManifest capabilityManifest = KubernetesMcpServerCapabilityManifest.V0066;

        Assert.Equal(
            installerManifest.RootElement.GetProperty("version").GetString(),
            capabilityManifest.Version);
        Assert.Equal(
            installerManifest.RootElement.GetProperty("checksums").GetProperty("linux-amd64").GetString(),
            capabilityManifest.LinuxAmd64Sha256);
        JsonElement contract = installerManifest.RootElement.GetProperty("capabilityContract");
        Assert.Equal(capabilityManifest.Tools.Count, contract.GetProperty("classifiedToolCount").GetInt32());
        Assert.Equal(
            capabilityManifest.Tools.Count(tool => tool.InPinnedSingleClusterSnapshot),
            contract.GetProperty("pinnedSingleClusterSnapshotToolCount").GetInt32());
        Assert.False(contract.GetProperty("disableDestructiveIsAuthorizationBoundary").GetBoolean());
    }

    [Fact]
    public async Task InstalledV0066_ToolsListMatchesPinnedSingleClusterSnapshot_WhenRequired()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(RequireLiveContractEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string repoRoot = FindRepoRoot();
        string binaryPath = Path.Combine(repoRoot, ".tools", "bin", "kubernetes-mcp-server");
        Assert.True(File.Exists(binaryPath), $"Pinned binary is missing at '{binaryPath}'.");

        string actualChecksum = Convert.ToHexStringLower(
            await SHA256.HashDataAsync(File.OpenRead(binaryPath), CancellationToken.None));
        Assert.Equal(KubernetesMcpServerCapabilityManifest.V0066.LinuxAmd64Sha256, actualChecksum);

        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "infragate-kubernetes-mcp-capability-contract",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            string kubeconfigPath = Path.Combine(testRoot, "single-context.config");
            await File.WriteAllTextAsync(kubeconfigPath, DummySingleContextKubeconfig);

            var descriptor = new DownstreamProcessDescriptor(
                "kubernetes-mcp-server-contract",
                binaryPath,
                [
                    "--kubeconfig", kubeconfigPath,
                    "--toolsets", "core,config,helm,kcp,kiali,kubevirt,netobserv,tekton",
                    "--disable-multi-cluster",
                    "--log-file", "stderr"
                ],
                repoRoot,
                AuthRequired: false,
                new HashSet<string>(StringComparer.Ordinal) { "PATH", "HOME", "TMPDIR", "TMP", "TEMP" },
                new Dictionary<string, string?>(StringComparer.Ordinal));
            await using var client = new DownstreamMcpClient(
                descriptor,
                new NullDownstreamServiceTokenProvider(),
                NullLogger<DownstreamMcpClient>.Instance,
                NullLoggerFactory.Instance,
                McpGatewayConventions.DownstreamSources.Secondary);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            IReadOnlyList<DownstreamTool> tools = await client.ListToolsAsync(timeout.Token);

            Assert.Equal(50, tools.Count);
            Assert.True(
                KubernetesMcpServerCapabilityManifest.V0066.TryValidatePinnedSingleClusterSnapshot(
                    tools,
                    out string error),
                error);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static DownstreamTool CreatePinnedPodsGetTool() => new(
        "pods_get",
        "Get a Pod in the current or provided namespace",
        IsReadOnly: true,
        IsDestructive: false,
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                name = new { description = "Name of the Pod", type = "string" },
                @namespace = new { description = "Namespace to get the Pod from", type = "string" }
            },
            required = new[] { "name" }
        }))
    {
        Annotations = JsonSerializer.SerializeToElement(new
        {
            destructiveHint = false,
            idempotentHint = false,
            openWorldHint = true,
            readOnlyHint = true,
            title = "Pods: Get"
        })
    };

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InfraGate.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private const string DummySingleContextKubeconfig = """
        apiVersion: v1
        kind: Config
        clusters:
        - name: contract
          cluster:
            server: https://127.0.0.1:9
            insecure-skip-tls-verify: true
        contexts:
        - name: contract
          context:
            cluster: contract
            user: contract
        current-context: contract
        users:
        - name: contract
          user:
            token: contract-only-token
        """;
}
