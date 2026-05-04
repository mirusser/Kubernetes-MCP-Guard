using System.Text.Json;
using System.Text.RegularExpressions;
using InfraGate.McpServer;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using InfraGate.McpServer.Tests;

#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008
namespace InfraGate.McpServer.Tests.IntegrationTests;

public sealed partial class McpServerIntegrationTests
{
    [Fact]
    public async Task McpServer_CanApplyApprovedK8sPlans_WhenIntegrationEnabled()
    {
        if (Environment.GetEnvironmentVariable("INFRA_GATE_RUN_INTEGRATION") != "1")
        {
            return;
        }

        var repoRoot = FindRepoRoot();
        var serverProject = Path.Combine(repoRoot, "src", "InfraGate.McpServer", "InfraGate.McpServer.csproj");
        var approvalRoot = Path.Combine(Path.GetTempPath(), "infra-gate-mcp", Guid.NewGuid().ToString("N"));
        const string namespaceName = K8sMcpOptions.DefaultNamespace;
        var kubeconfig = Environment.GetEnvironmentVariable("KUBECONFIG");
        if (string.IsNullOrWhiteSpace(kubeconfig))
        {
            kubeconfig = Path.Combine(repoRoot, ".kube", "mcp-nginx-demo.config");
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "infra-gate",
            Command = "dotnet",
            Arguments = ["run", "--project", serverProject],
            WorkingDirectory = repoRoot,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["K8S_MCP_APPROVAL_ROOT"] = approvalRoot,
                ["K8S_MCP_ALLOWED_NAMESPACES"] = namespaceName,
                ["KUBECONFIG"] = kubeconfig
            },
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        });

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);
        Assert.Contains(tools, tool => tool.Name == "request_apply_manifest");
        Assert.Contains(tools, tool => tool.Name == "get_allowed_namespaces");

        var allowedNsText = await CallTextAsync(
            client,
            "get_allowed_namespaces",
            new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);

        Assert.Contains(namespaceName, allowedNsText);
        Assert.Contains("allowedNamespaces", allowedNsText);

        var applyRequestText = await CallTextAsync(
            client,
            "request_apply_manifest",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["manifest"] = DemoManifest
            },
            cancellationToken: CancellationToken.None);

        var applyPlanId = await ApprovePlanAsync(approvalRoot, applyRequestText);
        var applyText = await CallTextAsync(
            client,
            "apply_approved_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = applyPlanId
            },
            cancellationToken: CancellationToken.None);

        Assert.Contains($"Applied plan: {applyPlanId}", applyText);
        Assert.Contains("Applied apps/v1 Deployment", applyText);

        var statusText = await CallTextAsync(
            client,
            "get_k8s_status",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["labelSelector"] = "app=mcp-api-demo"
            },
            cancellationToken: CancellationToken.None);

        Assert.Contains("mcp-api-demo", statusText);
        Assert.Contains("demo-config", statusText);

        var podName = TryGetFirstPodName(statusText);

        var eventsText = await CallTextAsync(
            client,
            "get_k8s_events",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["labelSelector"] = "app=mcp-api-demo",
                ["limit"] = 5
            },
            cancellationToken: CancellationToken.None);

        AssertJsonArrayProperty(eventsText, "events");

        var deploymentResourceText = await CallTextAsync(
            client,
            "get_k8s_resource",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["kind"] = "Deployment",
                ["name"] = "mcp-api-demo"
            },
            cancellationToken: CancellationToken.None);
        AssertJsonKindName(deploymentResourceText, "Deployment", "mcp-api-demo");

        var serviceResourceText = await CallTextAsync(
            client,
            "get_k8s_resource",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["kind"] = "Service",
                ["name"] = "mcp-api-demo"
            },
            cancellationToken: CancellationToken.None);
        AssertJsonKindName(serviceResourceText, "Service", "mcp-api-demo");

        var configMapResourceText = await CallTextAsync(
            client,
            "get_k8s_resource",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["kind"] = "ConfigMap",
                ["name"] = "demo-config"
            },
            cancellationToken: CancellationToken.None);
        AssertJsonKindName(configMapResourceText, "ConfigMap", "demo-config");

        var deploymentDiagnosticsText = await CallTextAsync(
            client,
            "get_deployment_diagnostics",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["name"] = "mcp-api-demo",
                ["limit"] = 5
            },
            cancellationToken: CancellationToken.None);
        AssertJsonKindName(deploymentDiagnosticsText, "Deployment", "mcp-api-demo");

        var serviceDiagnosticsText = await CallTextAsync(
            client,
            "get_service_diagnostics",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["name"] = "mcp-api-demo",
                ["limit"] = 5
            },
            cancellationToken: CancellationToken.None);
        AssertJsonKindName(serviceDiagnosticsText, "Service", "mcp-api-demo");

        if (!string.IsNullOrWhiteSpace(podName))
        {
            var podDiagnosticsText = await CallTextAsync(
                client,
                "get_pod_diagnostics",
                new Dictionary<string, object?>
                {
                    ["namespace"] = namespaceName,
                    ["podName"] = podName,
                    ["limit"] = 5
                },
                cancellationToken: CancellationToken.None);
            AssertJsonProperty(podDiagnosticsText, "kind", "Pod");
            AssertJsonProperty(podDiagnosticsText, "podName", podName);

            var podLogsText = await CallTextAsync(
                client,
                "get_pod_logs",
                new Dictionary<string, object?>
                {
                    ["namespace"] = namespaceName,
                    ["podName"] = podName,
                    ["container"] = "nginx",
                    ["tailLines"] = 10
                },
                cancellationToken: CancellationToken.None);
            AssertJsonProperty(podLogsText, "podName", podName);
        }

        var setImageRequestText = await CallTextAsync(
            client,
            "request_set_deployment_image",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["name"] = "mcp-api-demo",
                ["container"] = "nginx",
                ["image"] = "nginx:1.27-alpine"
            },
            cancellationToken: CancellationToken.None);
        var setImagePlanId = await ApprovePlanAsync(approvalRoot, setImageRequestText);
        var setImageText = await CallTextAsync(
            client,
            "apply_approved_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = setImagePlanId
            },
            cancellationToken: CancellationToken.None);

        Assert.Contains("Updated apps/v1 Deployment", setImageText);

        var scaleRequestText = await CallTextAsync(
            client,
            "request_scale_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["name"] = "mcp-api-demo",
                ["replicas"] = 2
            },
            cancellationToken: CancellationToken.None);
        var scalePlanId = await ApprovePlanAsync(approvalRoot, scaleRequestText);
        var scaleText = await CallTextAsync(
            client,
            "apply_approved_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = scalePlanId
            },
            cancellationToken: CancellationToken.None);

        Assert.Contains("Scaled apps/v1 Deployment", scaleText);

        var restartRequestText = await CallTextAsync(
            client,
            "request_restart_deployment",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["name"] = "mcp-api-demo"
            },
            cancellationToken: CancellationToken.None);
        var restartPlanId = await ApprovePlanAsync(approvalRoot, restartRequestText);
        var restartText = await CallTextAsync(
            client,
            "apply_approved_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = restartPlanId
            },
            cancellationToken: CancellationToken.None);

        Assert.Contains("Restarted apps/v1 Deployment", restartText);

        var deleteRequestText = await CallTextAsync(
            client,
            "request_delete_manifest",
            new Dictionary<string, object?>
            {
                ["namespace"] = namespaceName,
                ["manifest"] = DemoManifest
            },
            cancellationToken: CancellationToken.None);
        var deletePlanId = await ApprovePlanAsync(approvalRoot, deleteRequestText);
        var deleteText = await CallTextAsync(
            client,
            "apply_approved_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = deletePlanId
            },
            cancellationToken: CancellationToken.None);

        Assert.Contains("Deleted apps/v1 Deployment", deleteText);
        Assert.Contains("Deleted v1 Service", deleteText);
        Assert.Contains("Deleted v1 ConfigMap", deleteText);
    }

    private static async Task<string> CallTextAsync(
        McpClient client,
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

        return GetText(result);
    }

    private static async Task<string> ApprovePlanAsync(string approvalRoot, string requestText)
    {
        var planId = PlanIdPattern().Match(requestText).Groups["id"].Value;
        Assert.False(string.IsNullOrWhiteSpace(planId));

        var pendingPath = Path.Combine(approvalRoot, "pending", $"{planId}.json");
        var approvedPath = Path.Combine(approvalRoot, "approved", $"{planId}.sha256");
        var hash = await ApprovalStore.ComputeSha256Async(pendingPath, CancellationToken.None);
        await File.WriteAllTextAsync(approvedPath, hash, CancellationToken.None);

        return planId;
    }

    private static string GetText(CallToolResult result) =>
        string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(content => content.Text));

    private static string? TryGetFirstPodName(string statusText)
    {
        using var document = JsonDocument.Parse(statusText);
        foreach (var pod in document.RootElement.GetProperty("pods").EnumerateArray())
        {
            var podName = pod.GetProperty("name").GetString();
            if (!string.IsNullOrWhiteSpace(podName))
            {
                return podName;
            }
        }

        return null;
    }

    private static void AssertJsonKindName(string json, string kind, string name)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(kind, root.GetProperty("kind").GetString());
        Assert.Equal(name, root.GetProperty("name").GetString());
    }

    private static void AssertJsonProperty(string json, string propertyName, string expectedValue)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expectedValue, document.RootElement.GetProperty(propertyName).GetString());
    }

    private static void AssertJsonArrayProperty(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty(propertyName).ValueKind);
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

    [GeneratedRegex(@"PlanId:\s+(?<id>[0-9a-z-]+)", RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex PlanIdPattern();

    private const string DemoManifest = """
                                        apiVersion: apps/v1
                                        kind: Deployment
                                        metadata:
                                          name: mcp-api-demo
                                          labels:
                                            app: mcp-api-demo
                                        spec:
                                          replicas: 1
                                          selector:
                                            matchLabels:
                                              app: mcp-api-demo
                                          template:
                                            metadata:
                                              labels:
                                                app: mcp-api-demo
                                            spec:
                                              automountServiceAccountToken: false
                                              containers:
                                                - name: nginx
                                                  image: nginx:1.27-alpine
                                                  ports:
                                                    - containerPort: 80
                                        ---
                                        apiVersion: v1
                                        kind: Service
                                        metadata:
                                          name: mcp-api-demo
                                          labels:
                                            app: mcp-api-demo
                                        spec:
                                          selector:
                                            app: mcp-api-demo
                                          ports:
                                            - name: http
                                              port: 80
                                              targetPort: 80
                                        ---
                                        apiVersion: v1
                                        kind: ConfigMap
                                        metadata:
                                          name: demo-config
                                          labels:
                                            app: mcp-api-demo
                                        data:
                                          hello: world
                                        """;
}
