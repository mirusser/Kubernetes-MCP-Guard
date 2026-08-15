using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;

// ASPDEPR008: TestServer.Host is deprecated in favor of IHost. Suppressed for the same reason as
// GatewayHttpMcpIntegrationTests.cs (this file is a partial-class split of that class, and pragmas
// don't carry across files): TestServer is what CreateGatewayServer already returns.
#pragma warning disable ASPDEPR008

namespace InfraGate.McpGateway.Tests.IntegrationTests;

// Opt-in real end-to-end coverage for the secondary, read-only-only kubernetes-mcp-server
// downstream (see docs/adr for the decision record). Partial-class split of
// GatewayHttpMcpIntegrationTests: reuses its JWT/TestServer/MCP-client plumbing
// (CreateGatewayServer, CreateHttpMcpClientAsync, FindRepoRoot, FakeDownstream, etc.) rather than
// duplicating ~150 lines of auth/hosting setup for one new test.
//
// Two env vars gate the cluster-dependent scenario:
//   INFRA_GATE_RUN_GATEWAY_INTEGRATION=1     opt-in: run if prerequisites are present, else skip
//                                             with an explicit reported reason (never silently).
//   INFRA_GATE_REQUIRE_GATEWAY_INTEGRATION=1 required: fail loudly if prerequisites are missing,
//                                             for CI jobs that must prove the real contract holds.
public sealed partial class GatewayHttpMcpIntegrationTests
{
    private const string RunEnvVar = "INFRA_GATE_RUN_GATEWAY_INTEGRATION";
    private const string RequireEnvVar = "INFRA_GATE_REQUIRE_GATEWAY_INTEGRATION";
    private const string KubernetesContextName = "minikube-mcp";
    private const string DisallowedNamespaceName = "default";
    private const string KnownPodNamePrefix = "nginx-demo-";

    private readonly ITestOutputHelper _output;

    public GatewayHttpMcpIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Gateway_WithKubernetesMcpServerSecondary_EnforcesRealContract_WhenGatewayIntegrationEnabled()
    {
        bool required = Environment.GetEnvironmentVariable(RequireEnvVar) == "1";
        bool enabled = required || Environment.GetEnvironmentVariable(RunEnvVar) == "1";
        if (!enabled)
        {
            _output.WriteLine(
                $"SKIPPED: set {RunEnvVar}=1 to run this real-cluster contract test, or " +
                $"{RequireEnvVar}=1 to require it (fails instead of skipping when prerequisites are missing).");
            return;
        }

        string repoRoot = FindRepoRoot();
        string binaryPath = Path.Combine(repoRoot, ".tools", "bin", "kubernetes-mcp-server");
        if (!File.Exists(binaryPath))
        {
            if (required)
            {
                Assert.Fail(
                    $"{RequireEnvVar}=1 but the kubernetes-mcp-server binary is missing at '{binaryPath}'. " +
                    "Run scripts/install-kubernetes-mcp-server.sh first.");
            }

            _output.WriteLine(
                $"SKIPPED: kubernetes-mcp-server binary not found at '{binaryPath}'. " +
                $"Run scripts/install-kubernetes-mcp-server.sh, or unset {RunEnvVar} to skip quietly.");
            return;
        }

        string kubeconfigPath = Path.Combine(repoRoot, ".kube", "mcp-nginx-demo-viewer.config");
        if (!File.Exists(kubeconfigPath))
        {
            if (required)
            {
                Assert.Fail(
                    $"{RequireEnvVar}=1 but the viewer kubeconfig is missing at '{kubeconfigPath}'. " +
                    "Run scripts/create-demo-kubeconfig.sh first.");
            }

            _output.WriteLine($"SKIPPED: viewer kubeconfig not found at '{kubeconfigPath}'.");
            return;
        }

        string tomlPath = Path.Combine(
            Path.GetTempPath(), "infra-gate-k8s-mcp-tests", Guid.NewGuid().ToString("N") + ".toml");
        Directory.CreateDirectory(Path.GetDirectoryName(tomlPath)!);

        // Generate the real config via the RunProfiles CLI — the single source of truth for the
        // curated tool allowlist (see KubernetesMcpServerProfile.EnabledTools, Task 4). The
        // expected-allowlist below is parsed back out of this generated file, not hand-copied, so
        // the two cannot silently drift on a future version bump.
        await GenerateKubernetesMcpServerTomlAsync(repoRoot, tomlPath);
        string tomlContent = await File.ReadAllTextAsync(tomlPath);
        string[] expectedCuratedTools = ParseEnabledTools(tomlContent);
        Assert.NotEmpty(expectedCuratedTools);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerCommandKey}"] = binaryPath,
                [$"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerArgumentsKey}:0"] = McpGatewayConventions.SecondaryDownstream.ConfigArgument,
                [$"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerArgumentsKey}:1"] = tomlPath,
                [$"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerWorkingDirectoryKey}"] = repoRoot,
                [$"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerKubeconfigKey}"] = kubeconfigPath,
                [$"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerContextKey}"] = KubernetesContextName,
                [$"{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerSection}:{McpGatewayConventions.ConfigurationKeys.KubernetesMcpServerAllowedNamespacesKey}:0"] = NamespaceName,
            })
            .Build();

        var primaryDownstream = new FakeDownstream("primary result");
        var audit = new InMemoryAuditStore();

        // Exercises the exact production composition path (Task 16) rather than hand-assembling
        // the secondary's DownstreamMcpClient/registry/runner/policies — a config drift or a
        // startup-validator regression in RegisterKubernetesMcpServerDownstream would fail this
        // test, not just a lower-level unit test.
        using TestServer server = CreateGatewayServer(
            primaryDownstream,
            audit,
            configureAdditionalServices: services => services.RegisterKubernetesMcpServerDownstream(configuration));
        await using var client = await CreateHttpMcpClientAsync(server);

        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);
        var toolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        // (a) Response shape: existing InfraGate tools are still present alongside the curated set,
        // and no request_* wrapper exists for any secondary tool (read-only-only, never routed
        // through the mutation-approval path).
        Assert.Contains("get_allowed_namespaces", toolNames);
        foreach (string curatedTool in expectedCuratedTools)
        {
            Assert.Contains(curatedTool, toolNames);
            Assert.DoesNotContain(McpGatewayConventions.ToolNames.RequestToolPrefix + curatedTool, toolNames);
        }

        // (b) Allowlist is exhaustive: the registry behind the secondary source never advertises a
        // tool outside the curated set.
        var secondaryRegistry = server.Host.Services.GetRequiredKeyedService<DownstreamToolRegistry>(
            McpGatewayConventions.SecondaryDownstream.ServiceKey);
        IReadOnlyList<DownstreamTool> secondaryAllTools = await secondaryRegistry.GetReadOnlyAsync(CancellationToken.None);
        foreach (DownstreamTool secondaryTool in secondaryAllTools)
        {
            Assert.Contains(secondaryTool.Name, expectedCuratedTools);
        }

        // (c) Positive path: a curated, in-scope, namespaced read succeeds and returns real data.
        CallToolResult podsListResult = await client.CallToolAsync(
            McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool,
            new Dictionary<string, object?> { ["namespace"] = NamespaceName },
            cancellationToken: CancellationToken.None);
        string podsListText = TextOf(podsListResult);
        Assert.False(podsListResult.IsError);
        Assert.Contains(KnownPodNamePrefix, podsListText, StringComparison.Ordinal);
        _output.WriteLine($"pods_list_in_namespace succeeded: {podsListText}");

        // (d) Cluster-wide list is denied: kubernetes-mcp-server's own upstream tool for an
        // unscoped `pods_list` is never enabled in the curated TOML, so it's unknown to the
        // catalog entirely — the same denial path as any nonexistent tool.
        await AssertDeniedAsUnknownToolAsync(client, "pods_list", new Dictionary<string, object?>());

        // (e) Raw/Secret-resource read is denied for the same reason: `resources_get` is never
        // enabled in the curated TOML.
        await AssertDeniedAsUnknownToolAsync(
            client,
            "resources_get",
            new Dictionary<string, object?> { ["namespace"] = NamespaceName, ["kind"] = "Secret" });

        // (f) Mutation is denied for the same reason: destructive tools are never enabled in the
        // curated TOML.
        await AssertDeniedAsUnknownToolAsync(
            client,
            "pods_delete",
            new Dictionary<string, object?> { ["namespace"] = NamespaceName, ["name"] = "nginx-demo" });

        // (g) Namespace escape: a curated tool, but outside the configured allowlist, is denied by
        // KubernetesMcpServerRequestPolicy before ever reaching the real binary.
        CallToolResult namespaceEscapeResult = await client.CallToolAsync(
            McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool,
            new Dictionary<string, object?> { ["namespace"] = DisallowedNamespaceName },
            cancellationToken: CancellationToken.None);
        Assert.True(namespaceEscapeResult.IsError);
        Assert.Equal(
            McpGatewayMessages.KubernetesMcpServerPolicy.NamespaceNotAllowed(DisallowedNamespaceName),
            TextOf(namespaceEscapeResult));

        // (h) Absent log tail is denied by request policy (required argument), never reaching the
        // real binary.
        CallToolResult absentTailResult = await client.CallToolAsync(
            McpGatewayConventions.SecondaryDownstream.PodsLogTool,
            new Dictionary<string, object?> { ["namespace"] = NamespaceName, ["name"] = "nginx-demo" },
            cancellationToken: CancellationToken.None);
        Assert.True(absentTailResult.IsError);
        Assert.Equal(McpGatewayMessages.KubernetesMcpServerPolicy.LogTailOutOfRange, TextOf(absentTailResult));

        // (i) Oversized log tail is denied by the same request-policy rule (bounded 0-200).
        CallToolResult oversizedTailResult = await client.CallToolAsync(
            McpGatewayConventions.SecondaryDownstream.PodsLogTool,
            new Dictionary<string, object?> { ["namespace"] = NamespaceName, ["name"] = "nginx-demo", ["tail"] = 99999 },
            cancellationToken: CancellationToken.None);
        Assert.True(oversizedTailResult.IsError);
        Assert.Equal(McpGatewayMessages.KubernetesMcpServerPolicy.LogTailOutOfRange, TextOf(oversizedTailResult));
    }

    // (j) Oversized result is denied by the real KubernetesMcpServerResponsePolicy — exercised
    // directly (deterministic, no cluster required) so this suite proves the guarantee on its own
    // even when the opt-in cluster scenario above is skipped.
    [Fact]
    public void KubernetesMcpServerResponsePolicy_OversizedResult_IsDenied()
    {
        var policy = new KubernetesMcpServerResponsePolicy();
        string oversized = new('a', KubernetesMcpServerResponsePolicy.MaximumResponseBytes + 1);

        KubernetesMcpServerResponsePolicyResult result = policy.Apply(
            McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool,
            oversized);

        Assert.False(result.IsAllowed);
        Assert.Equal(
            McpGatewayMessages.KubernetesMcpServerPolicy.ResponseTooLarge(
                oversized.Length,
                KubernetesMcpServerResponsePolicy.MaximumResponseBytes),
            result.Error);
    }

    private static async Task AssertDeniedAsUnknownToolAsync(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal(McpGatewayMessages.ToolRouting.UnknownTool(toolName), TextOf(result));
    }

    private static string TextOf(CallToolResult result) =>
        string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(content => content.Text));

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
