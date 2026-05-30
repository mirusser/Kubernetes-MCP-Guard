using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.McpGateway;
using InfraGate.McpServer;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace InfraGate.Safety.E2E.Tests.Workflows;

[Trait("Category", "SafetyE2E")]
[Collection(SafetyE2ECollection.Name)]
public sealed class RbacMatrixTests(SafetyE2EFixture fixture)
{
    [Fact]
    public async Task ApplyApprovedPlan_WithReadOnlyServiceAccount_ReturnsK8sForbidden()
    {
        if (!fixture.IsEnabled)
        {
            return;
        }

        var viewKubeconfig = CreateReadOnlyKubeconfig();
        if (viewKubeconfig is null)
        {
            return;
        }

        var repoRoot = FindRepoRoot();
        var serverProject = Path.Combine(repoRoot, "src", "InfraGate.McpServer", "InfraGate.McpServer.csproj");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "infra-gate-rbac-view",
            Command = "dotnet",
            Arguments = ["run", "--project", serverProject],
            WorkingDirectory = repoRoot,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["INFRA_GATE_ENVIRONMENT"] = "Development",
                ["K8S_MCP_APPROVAL_ROOT"] = fixture.ApprovalRoot,
                ["K8S_MCP_ALLOWED_NAMESPACES"] = fixture.Namespace,
                ["KUBECONFIG"] = viewKubeconfig
            },
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        });

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        var requestText = await CallTextAsync(
            client,
            "request_restart_deployment",
            new Dictionary<string, object?>
            {
                [KubernetesAdapterConventions.ToolArguments.Namespace] = fixture.Namespace,
                [KubernetesAdapterConventions.ToolArguments.Name] = "nginx-demo"
            });

        Assert.Contains("PlanId:", requestText, StringComparison.Ordinal);
        var planId = SafetyE2EFixture.ParsePlanId(requestText);

        var pending = await fixture.ApprovalStore.GetPendingPlanAsync(planId, CancellationToken.None);
        if (!pending.IsPending || pending.Envelope is null)
        {
            throw new InvalidOperationException(pending.Message);
        }

        await fixture.ApprovalStore.CreateGrantAsync(
            pending.Envelope,
            pending.Envelope.Requester.Subject,
            sourceChallengeId: "rbac-matrix",
            CancellationToken.None);

        var applyText = await CallTextAsync(
            client,
            McpGatewayConventions.ToolNames.ApplyApprovedPlan,
            new Dictionary<string, object?>
            {
                [McpGatewayConventions.ToolArguments.PlanId] = planId
            });

        Assert.Contains("Refused", applyText, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.ApprovalStore.GetAppliedPath(planId)));
    }

    private static async Task<string> CallTextAsync(
        McpClient client,
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

        return string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>().Select(content => content.Text));
    }

    private string? CreateReadOnlyKubeconfig()
    {
        var namespaceName = fixture.Namespace;
        const string saName = "infra-gate-mcp-view";

        // Verify the SA exists and has no write verbs.
        var token = GetServiceAccountToken(namespaceName, saName);
        if (token is null)
        {
            return null;
        }

        var server = GetServerUrl();
        var caData = GetCertificateAuthorityData();
        if (server is null || caData is null)
        {
            return null;
        }

        var kubeconfigPath = Path.Combine(Path.GetTempPath(), $"infra-gate-safety-e2e-rbac-view-{Guid.NewGuid():N}.config");
        var kubeconfig = $@"apiVersion: v1
kind: Config
clusters:
  - name: kind-safety
    cluster:
      server: {server}
      certificate-authority-data: {caData}
users:
  - name: {saName}
    user:
      token: {token}
contexts:
  - name: kind-safety-view
    context:
      cluster: kind-safety
      user: {saName}
      namespace: {namespaceName}
current-context: kind-safety-view
";

        Directory.CreateDirectory(Path.GetDirectoryName(kubeconfigPath)!);
        File.WriteAllText(kubeconfigPath, kubeconfig);

        return kubeconfigPath;
    }

    private static string? GetServiceAccountToken(string namespaceName, string saName)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "kubectl",
                Arguments = $"-n {namespaceName} create token {saName} --duration=1h",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(15));

        if (process.ExitCode != 0)
        {
            return null;
        }

        return output.Trim();
    }

    private static string? GetServerUrl()
    {
        return RunKubectl("config view --minify -o jsonpath='{.clusters[0].cluster.server}'");
    }

    private static string? GetCertificateAuthorityData()
    {
        return RunKubectl("config view --raw --minify -o jsonpath='{.clusters[0].cluster.certificate-authority-data}'");
    }

    private static string? RunKubectl(string args)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "kubectl",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(TimeSpan.FromSeconds(10));

        return string.IsNullOrWhiteSpace(output) ? null : output;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InfraGate.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
