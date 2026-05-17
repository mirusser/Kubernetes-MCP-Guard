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

    [Fact]
    public async Task ExecuteAsync_GenerateWithExistingForeignFile_ReturnsErrorWithoutForce()
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
                      kubeconfig: .kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "local-stdio.env");
        await File.WriteAllTextAsync(outputPath, "# Hand-written file\nSOME_VAR=value\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("will not overwrite", error.ToString(), StringComparison.OrdinalIgnoreCase);
        string fileContent = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Hand-written file", fileContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithExistingWrongProfileFile_ReturnsErrorWithoutForce()
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
                      kubeconfig: .kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "local-stdio.env");
        await File.WriteAllTextAsync(outputPath,
            "# Generated from run-profiles.yaml profile: local-compose\n# Do not edit.\n\nSOME_VAR=value\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("will not overwrite", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithMatchingGeneratedHeader_OverwritesAutomatically()
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
                      kubeconfig: .kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "local-stdio.env");
        await File.WriteAllTextAsync(outputPath,
            "# Generated from run-profiles.yaml profile: local-stdio\n# Do not edit.\n\nOLD_VAR=old\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string fileContent = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("OLD_VAR", fileContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithForceFlag_OverwritesForeignFile()
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
                      kubeconfig: .kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "local-stdio.env");
        await File.WriteAllTextAsync(outputPath, "# Hand-written file\nSOME_VAR=value\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath, "--force"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string fileContent = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("Hand-written file", fileContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithGatewaySection_EmitsGatewayEnvVars()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                gateway:
                  aspnetcoreUrls: http://0.0.0.0:3001
                  downstreamAssembly: /app/server/InfraGate.McpServer.dll
                  guardAuditRoot: /data/guardrails
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        HashSet<string> keys = ParseEnvKeys(await File.ReadAllTextAsync(outputPath));
        Assert.Contains("ASPNETCORE_URLS", keys);
        Assert.Contains("INFRA_GATE_DOWNSTREAM_ASSEMBLY", keys);
        Assert.Contains("INFRA_GATE_GUARD_AUDIT_ROOT", keys);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithIdentityProviderSection_EmitsOauthEnvVars()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                identityProvider:
                  authority: http://127.0.0.1:3010/realms/infra-gate
                  metadataAddress: http://keycloak:8080/realms/infra-gate/.well-known/openid-configuration
                  resource: http://127.0.0.1:3001/mcp
                  scope: mcp:tools
                  requireHttpsMetadata: "false"
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        HashSet<string> keys = ParseEnvKeys(await File.ReadAllTextAsync(outputPath));
        Assert.Contains("INFRA_GATE_OAUTH_AUTHORITY", keys);
        Assert.Contains("INFRA_GATE_OAUTH_METADATA_ADDRESS", keys);
        Assert.Contains("INFRA_GATE_OAUTH_RESOURCE", keys);
        Assert.Contains("INFRA_GATE_OAUTH_SCOPE", keys);
        Assert.Contains("INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA", keys);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithApprovalAuthoritySection_EmitsApprovalEnvVars()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                approvalAuthority:
                  baseUrl: http://127.0.0.1:3001
                  oauthClientId: infra-gate-approval-ui
                  oauthCallbackPath: /approvals/oauth/callback
                  oauthAuthorizationEndpoint: http://127.0.0.1:3010/realms/infra-gate/protocol/openid-connect/auth
                  oauthTokenEndpoint: http://keycloak:8080/realms/infra-gate/protocol/openid-connect/token
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        HashSet<string> keys = ParseEnvKeys(await File.ReadAllTextAsync(outputPath));
        Assert.Contains("INFRA_GATE_APPROVAL_BASE_URL", keys);
        Assert.Contains("INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID", keys);
        Assert.Contains("INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH", keys);
        Assert.Contains("INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT", keys);
        Assert.Contains("INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT", keys);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithHostSection_EmitsComposeInterpolationVars()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                host:
                  bindAddress: 127.0.0.1
                  bindPort: "3001"
                  gatewayImage: kubernetes-mcp-guard-gateway
                  kubeconfigHostPath: .kube/mcp-nginx-demo.compose.config
                  approvalHostPath: .mcp-approvals
                  guardAuditHostPath: .mcp-guardrails
                  dataProtectionHostPath: .mcp-dataprotection-keys
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        HashSet<string> keys = ParseEnvKeys(await File.ReadAllTextAsync(outputPath));
        Assert.Contains("INFRA_GATE_BIND_ADDRESS", keys);
        Assert.Contains("INFRA_GATE_BIND_PORT", keys);
        Assert.Contains("INFRA_GATE_GATEWAY_IMAGE", keys);
        Assert.Contains("INFRA_GATE_KUBECONFIG_HOST_PATH", keys);
        Assert.Contains("INFRA_GATE_APPROVAL_HOST_PATH", keys);
        Assert.Contains("INFRA_GATE_GUARD_AUDIT_HOST_PATH", keys);
        Assert.Contains("INFRA_GATE_DATA_PROTECTION_HOST_PATH", keys);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithDefaults_InheritsDefaultValues()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            defaults:
              gateway:
                aspnetcoreUrls: http://0.0.0.0:3001
                downstreamAssembly: /app/server/InfraGate.McpServer.dll
              identityProvider:
                scope: mcp:tools
                requireHttpsMetadata: "false"
            profiles:
              local-compose:
                kind: compose
                gateway: {}
                identityProvider:
                  authority: http://127.0.0.1:3010/realms/infra-gate
                  metadataAddress: http://keycloak:8080/realms/infra-gate/.well-known/openid-configuration
                  resource: http://127.0.0.1:3001/mcp
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("ASPNETCORE_URLS=http://0.0.0.0:3001", content, StringComparison.Ordinal);
        Assert.Contains("INFRA_GATE_OAUTH_SCOPE=mcp:tools", content, StringComparison.Ordinal);
        Assert.Contains("INFRA_GATE_OAUTH_AUTHORITY=http://127.0.0.1:3010/realms/infra-gate", content, StringComparison.Ordinal);
    }

    private static readonly HashSet<string> ComposeStackProfileKeys =
        new(StringComparer.Ordinal)
        {
            "INFRA_GATE_ENVIRONMENT",
            "ASPNETCORE_URLS",
            "INFRA_GATE_DOWNSTREAM_ASSEMBLY",
            "INFRA_GATE_GUARD_AUDIT_ROOT",
            "INFRA_GATE_OAUTH_AUTHORITY",
            "INFRA_GATE_OAUTH_METADATA_ADDRESS",
            "INFRA_GATE_OAUTH_RESOURCE",
            "INFRA_GATE_OAUTH_SCOPE",
            "INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA",
            "INFRA_GATE_APPROVAL_BASE_URL",
            "INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID",
            "INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH",
            "INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT",
            "INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT",
            "K8S_MCP_APPROVAL_ROOT",
            "KUBECONFIG",
            "K8S_MCP_ALLOWED_NAMESPACES",
            "INFRA_GATE_BIND_ADDRESS",
            "INFRA_GATE_BIND_PORT",
            "INFRA_GATE_GATEWAY_IMAGE",
            "INFRA_GATE_KUBECONFIG_HOST_PATH",
            "INFRA_GATE_APPROVAL_HOST_PATH",
            "INFRA_GATE_GUARD_AUDIT_HOST_PATH",
            "INFRA_GATE_DATA_PROTECTION_HOST_PATH"
        };

    private static readonly HashSet<string> SourceGatewayProfileKeys =
        new(StringComparer.Ordinal)
        {
            "INFRA_GATE_ENVIRONMENT",
            "ASPNETCORE_URLS",
            "INFRA_GATE_DOWNSTREAM_ASSEMBLY",
            "INFRA_GATE_GUARD_AUDIT_ROOT",
            "INFRA_GATE_OAUTH_AUTHORITY",
            "INFRA_GATE_OAUTH_METADATA_ADDRESS",
            "INFRA_GATE_OAUTH_RESOURCE",
            "INFRA_GATE_OAUTH_SCOPE",
            "INFRA_GATE_OAUTH_REQUIRE_HTTPS_METADATA",
            "INFRA_GATE_APPROVAL_BASE_URL",
            "INFRA_GATE_APPROVAL_OAUTH_CLIENT_ID",
            "INFRA_GATE_APPROVAL_OAUTH_CALLBACK_PATH",
            "INFRA_GATE_APPROVAL_OAUTH_AUTHORIZATION_ENDPOINT",
            "INFRA_GATE_APPROVAL_OAUTH_TOKEN_ENDPOINT",
            "K8S_MCP_APPROVAL_ROOT",
            "KUBECONFIG",
            "K8S_MCP_ALLOWED_NAMESPACES"
        };

    private static readonly HashSet<string> MinimalProfileKeys =
        new(StringComparer.Ordinal)
        {
            "INFRA_GATE_ENVIRONMENT",
            "K8S_MCP_APPROVAL_ROOT",
            "KUBECONFIG",
            "K8S_MCP_ALLOWED_NAMESPACES"
        };

    public static IEnumerable<object[]> ProfileKeySetData =>
    [
        ["local-compose", ComposeStackProfileKeys],
        ["local-source-gateway", SourceGatewayProfileKeys],
        ["development", ComposeStackProfileKeys],
        ["production", ComposeStackProfileKeys],
        ["test-integration", MinimalProfileKeys],
        ["test-gateway-integration", MinimalProfileKeys],
        ["test-safety-e2e", MinimalProfileKeys],
        ["smoke-local", ComposeStackProfileKeys],
        ["smoke-release", ComposeStackProfileKeys]
    ];

    [Theory]
    [MemberData(nameof(ProfileKeySetData))]
    public async Task ExecuteAsync_GenerateProfile_EmitsExpectedEnvKeys(
        string profileName,
        HashSet<string> expectedKeys)
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"{profileName}-{Guid.NewGuid()}.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", profileName, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Equal(expectedKeys, ParseEnvKeys(content));
    }

    [Fact]
    public async Task ExecuteAsync_ValidateWithDuplicateProfileNames_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-stdio:
                kind: mcp-stdio
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: .kube/config
                      allowedNamespaces:
                        - default
              local-stdio:
                kind: compose
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: .kube/config
                      allowedNamespaces:
                        - default
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["validate", "--config", configPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Duplicate profile name: local-stdio", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSet_OverridesProfileValue()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                host:
                  bindAddress: 127.0.0.1
                  bindPort: "3001"
                  gatewayImage: my-image
                  kubeconfigHostPath: .kube/config
                  approvalHostPath: .approvals
                  guardAuditHostPath: .guardrails
                  dataProtectionHostPath: .dataprotection
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--output", outputPath,
             "--set", "host.bindAddress=10.0.0.1"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("INFRA_GATE_BIND_ADDRESS=10.0.0.1", content, StringComparison.Ordinal);
        Assert.DoesNotContain("INFRA_GATE_BIND_ADDRESS=127.0.0.1", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithMultipleSet_AllOverridesApplied()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                identityProvider:
                  authority: http://127.0.0.1:3010/realms/infra-gate
                  metadataAddress: http://keycloak:8080/realms/infra-gate/.well-known/openid-configuration
                  resource: http://127.0.0.1:3001/mcp
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--output", outputPath,
             "--set", "identityProvider.authority=http://172.17.0.1:3010/realms/infra-gate",
             "--set", "identityProvider.metadataAddress=http://172.17.0.1:3010/realms/infra-gate/.well-known/openid-configuration"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("INFRA_GATE_OAUTH_AUTHORITY=http://172.17.0.1:3010/realms/infra-gate", content, StringComparison.Ordinal);
        Assert.Contains("INFRA_GATE_OAUTH_METADATA_ADDRESS=http://172.17.0.1:3010/realms/infra-gate/.well-known/openid-configuration", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithUnknownSetPath_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-stdio:
                kind: mcp-stdio
                genericApprovalCore:
                  approvalRoot: .mcp-approvals
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: .kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath,
             "--set", "unknown.field=value"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown.field", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetGatewayField_OverridesGatewayValue()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                gateway:
                  aspnetcoreUrls: http://0.0.0.0:3001
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--output", outputPath,
             "--set", "gateway.aspnetcoreUrls=http://0.0.0.0:9090"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("ASPNETCORE_URLS=http://0.0.0.0:9090", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetApprovalAuthorityField_OverridesValue()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                approvalAuthority:
                  baseUrl: http://127.0.0.1:3001
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--output", outputPath,
             "--set", "approvalAuthority.baseUrl=http://0.0.0.0:4000"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("INFRA_GATE_APPROVAL_BASE_URL=http://0.0.0.0:4000", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetGenericApprovalCoreField_OverridesValue()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-stdio:
                kind: mcp-stdio
                genericApprovalCore:
                  approvalRoot: .mcp-approvals
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: .kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath,
             "--set", "genericApprovalCore.approvalRoot=/custom/path"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("K8S_MCP_APPROVAL_ROOT=/custom/path", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithMalformedSet_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-stdio:
                kind: mcp-stdio
                genericApprovalCore:
                  approvalRoot: .mcp-approvals
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: .kube/config
                      allowedNamespaces:
                        - default
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath,
             "--set", "noequals"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--set", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateSmokeRelease_MatchesCommittedReleaseExample()
    {
        string repoRoot = FindRepoRoot();
        string examplePath = Path.Combine(repoRoot, "deploy", "local-oauth", "release.env.example");
        string outputPath = Path.Combine(Path.GetTempPath(), $"smoke-release-{Guid.NewGuid()}.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "smoke-release", "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());

        string generated = await File.ReadAllTextAsync(outputPath);
        string committed = await File.ReadAllTextAsync(examplePath);
        Assert.Equal(
            committed.ReplaceLineEndings(),
            generated.ReplaceLineEndings());
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "deploy", "run-profiles.yaml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from test output directory.");
    }

    private static HashSet<string> ParseEnvKeys(string envContent) =>
        envContent.Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#') && line.Contains('='))
            .Select(line => line.Split('=')[0].Trim())
            .Where(key => !string.IsNullOrEmpty(key))
            .ToHashSet(StringComparer.Ordinal);

    private static async Task<string> WriteConfigAsync(string content)
    {
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "run-profiles.yaml");
        await File.WriteAllTextAsync(path, content);

        return path;
    }
}
