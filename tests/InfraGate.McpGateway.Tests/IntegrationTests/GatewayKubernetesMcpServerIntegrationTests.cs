using System.Text.RegularExpressions;
using InfraGate.McpGateway.DownstreamAuth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.IntegrationTests;

// Opt-in (INFRA_GATE_RUN_GATEWAY_INTEGRATION=1) real end-to-end coverage for the secondary,
// read-only-only kubernetes-mcp-server downstream (see docs/adr for the decision record).
// Partial-class split of GatewayHttpMcpIntegrationTests: reuses its JWT/TestServer/MCP-client
// plumbing (CreateGatewayServer, CreateHttpMcpClientAsync, FindRepoRoot, FakeDownstream, etc.)
// rather than duplicating ~150 lines of auth/hosting setup for one new test.
public sealed partial class GatewayHttpMcpIntegrationTests
{
    [Fact]
    public async Task Gateway_WithKubernetesMcpServerSecondary_ListsAndCallsCuratedReadOnlyTools_WhenGatewayIntegrationEnabled()
    {
        if (Environment.GetEnvironmentVariable("INFRA_GATE_RUN_GATEWAY_INTEGRATION") != "1")
        {
            return;
        }

        string repoRoot = FindRepoRoot();
        string binaryPath = Path.Combine(repoRoot, ".tools", "bin", "kubernetes-mcp-server");
        if (!File.Exists(binaryPath))
        {
            // Not installed — scripts/install-kubernetes-mcp-server.sh was not run. Skip cleanly
            // rather than fail; this mirrors the existing INFRA_GATE_RUN_GATEWAY_INTEGRATION gate.
            return;
        }

        string tomlPath = Path.Combine(
            Path.GetTempPath(), "infra-gate-k8s-mcp-tests", Guid.NewGuid().ToString("N") + ".toml");
        Directory.CreateDirectory(Path.GetDirectoryName(tomlPath)!);

        // Generate the real config via the RunProfiles CLI — the single source of truth for the
        // curated tool allowlist (see KubernetesMcpServerProfile.EnabledTools, Task 4). The
        // expected-allowlist below is parsed back out of this generated file, not hand-copied,
        // so the two cannot silently drift on a future version bump.
        await GenerateKubernetesMcpServerTomlAsync(repoRoot, tomlPath);
        string tomlContent = await File.ReadAllTextAsync(tomlPath);
        string[] expectedCuratedTools = ParseEnabledTools(tomlContent);
        Assert.NotEmpty(expectedCuratedTools);

        var secondaryOptions = new KubernetesMcpServerProcessOptions(
            binaryPath,
            ["--config", tomlPath],
            repoRoot,
            Path.Combine(repoRoot, ".kube", "mcp-nginx-demo-viewer.config"),
            "minikube-mcp",
            new HashSet<string>([NamespaceName], StringComparer.Ordinal));
        DownstreamProcessDescriptor secondaryDescriptor =
            DownstreamProcessDescriptor.ForKubernetesMcpServer(secondaryOptions);
        await using var secondaryDownstream = new DownstreamMcpClient(
            secondaryDescriptor,
            new NullDownstreamServiceTokenProvider(),
            NullLogger<DownstreamMcpClient>.Instance,
            NullLoggerFactory.Instance);
        var secondaryRegistry = new DownstreamToolRegistry(secondaryDownstream);
        var secondaryRunner = new GuardedToolRunner(
            secondaryDownstream,
            new InMemoryAuditStore(),
            httpContextAccessor: null,
            new SensitiveDataRedactor(McpGatewayConventions.SensitiveDataRedaction.Defaults, NullLogger<SensitiveDataRedactor>.Instance),
            NullLogger<GuardedToolRunner>.Instance);

        var primaryDownstream = new FakeDownstream("primary result");
        var audit = new InMemoryAuditStore();
        using TestServer server = CreateGatewayServer(
            primaryDownstream,
            audit,
            configureAdditionalServices: services =>
            {
                services.AddKeyedSingleton(McpGatewayConventions.SecondaryDownstream.ServiceKey, secondaryRegistry);
                services.AddKeyedSingleton(McpGatewayConventions.SecondaryDownstream.ServiceKey, secondaryRunner);
                services.AddKeyedSingleton(
                    McpGatewayConventions.SecondaryDownstream.ServiceKey,
                    new KubernetesMcpServerRequestPolicy(secondaryOptions.AllowedNamespaces));
                services.AddKeyedSingleton(
                    McpGatewayConventions.SecondaryDownstream.ServiceKey,
                    new KubernetesMcpServerResponsePolicy());
            });
        await using var client = await CreateHttpMcpClientAsync(server);

        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);
        var toolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        // (a) Response shape: existing InfraGate tools are still present alongside the curated set.
        Assert.Contains("get_allowed_namespaces", toolNames);
        foreach (string curatedTool in expectedCuratedTools)
        {
            Assert.Contains(curatedTool, toolNames);
        }

        // (b) Allowlist is exhaustive: no non-curated or destructive kubernetes-mcp-server tool
        // is ever listed, and no request_* wrapper exists for any secondary tool.
        IReadOnlyList<DownstreamTool> secondaryAllTools = await secondaryRegistry.GetReadOnlyAsync(CancellationToken.None);
        foreach (DownstreamTool secondaryTool in secondaryAllTools)
        {
            Assert.Contains(secondaryTool.Name, expectedCuratedTools);
            Assert.DoesNotContain(McpGatewayConventions.ToolNames.RequestToolPrefix + secondaryTool.Name, toolNames);
        }

        string podsListText = await CallTextAsync(
            client,
            "pods_list_in_namespace",
            new Dictionary<string, object?> { ["namespace"] = NamespaceName });

        Assert.False(string.IsNullOrWhiteSpace(podsListText));
        Assert.DoesNotContain("DownstreamCallFailed", podsListText, StringComparison.Ordinal);
    }

    private static async Task GenerateKubernetesMcpServerTomlAsync(string repoRoot, string outputPath)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add("src/InfraGate.RunProfiles");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("generate-toml");
        startInfo.ArgumentList.Add("local-source-gateway");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--force");

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start InfraGate.RunProfiles generate-toml.");
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(
                $"InfraGate.RunProfiles generate-toml failed (exit {process.ExitCode}): {stderr}");
        }
    }

    private static string[] ParseEnabledTools(string tomlContent)
    {
        Match match = EnabledToolsPattern().Match(tomlContent);
        if (!match.Success)
        {
            throw new InvalidOperationException("Generated TOML did not contain an enabled_tools line.");
        }

        return match.Groups["names"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.Trim('"'))
            .ToArray();
    }

    [GeneratedRegex(@"enabled_tools\s*=\s*\[(?<names>[^\]]*)\]", RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex EnabledToolsPattern();
}
