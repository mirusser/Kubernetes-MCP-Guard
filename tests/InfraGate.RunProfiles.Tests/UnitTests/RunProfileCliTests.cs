using InfraGate.RunProfiles;

namespace InfraGate.RunProfiles.Tests.UnitTests;

public sealed class RunProfileCliTests
{
    [Fact]
    public async Task ExecuteAsync_List_PrintsProfiles()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: .kube/mcp-nginx-demo.config
                      allowedNamespaces:
                        - mcp-nginx-demo
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["list", "--config", configPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("local-compose", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ValidateWithUnknownRootKey_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            unexpected: true
            profiles:
              local-compose:
                kind: compose
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["validate", "--config", configPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Unknown YAML key: unexpected", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateLocalStdio_WritesDeterministicEnvFile()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-stdio:
                kind: mcp-stdio
                runtimeMode: Development
                genericApprovalCore:
                  approvalRoot: .mcp-approvals
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: .kube/mcp-nginx-demo.config
                      allowedNamespaces:
                        - mcp-nginx-demo
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "local-stdio.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.Equal(
            """
            # Generated from run-profiles.yaml profile: local-stdio
            # Do not edit. Run: dotnet run --project src/InfraGate.RunProfiles -- generate local-stdio

            # Runtime
            INFRA_GATE_ENVIRONMENT=Development

            # Generic Approval Core
            K8S_MCP_APPROVAL_ROOT=.mcp-approvals

            # Kubernetes Adapter
            KUBECONFIG=.kube/mcp-nginx-demo.config
            K8S_MCP_ALLOWED_NAMESPACES=mcp-nginx-demo

            """.ReplaceLineEndings(),
            (await File.ReadAllTextAsync(outputPath)).ReplaceLineEndings());
    }

    [Fact]
    public async Task ExecuteAsync_ValidateWithUnsupportedDomainAdapter_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-stdio:
                kind: mcp-stdio
                domainAdapters:
                  - name: dockerAdapter
                    type: docker
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["validate", "--config", configPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Unsupported Domain Adapter type: docker", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ValidateWithoutDomainAdapter_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-stdio:
                kind: mcp-stdio
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["validate", "--config", configPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Run Profile 'local-stdio' must declare exactly one Domain Adapter.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ListWithRepositoryRunProfiles_PrintsInitialProfiles()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["list"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        string profileList = output.ToString();
        Assert.Contains("local-compose", profileList, StringComparison.Ordinal);
        Assert.Contains("local-source-gateway", profileList, StringComparison.Ordinal);
        Assert.Contains("local-stdio", profileList, StringComparison.Ordinal);
        Assert.Contains("development", profileList, StringComparison.Ordinal);
        Assert.Contains("production", profileList, StringComparison.Ordinal);
        Assert.Contains("test-integration", profileList, StringComparison.Ordinal);
        Assert.Contains("test-gateway-integration", profileList, StringComparison.Ordinal);
        Assert.Contains("test-safety-e2e", profileList, StringComparison.Ordinal);
        Assert.Contains("smoke-local", profileList, StringComparison.Ordinal);
        Assert.Contains("smoke-release", profileList, StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    private static async Task<string> WriteConfigAsync(string content)
    {
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "run-profiles.yaml");
        await File.WriteAllTextAsync(path, content);

        return path;
    }
}
