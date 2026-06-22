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

        Assert.True(0 == exitCode, $"CLI exited with {exitCode}. Error output: {error}");
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
    public async Task ExecuteAsync_ValidateWithOpenRouterScalar_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                openRouter: openrouter-key
                domainAdapters:
                  - name: kubernetesAdapter
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /run/kube/config
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
        Assert.Empty(output.ToString());
        Assert.Contains("YAML key 'openRouter' must be a mapping.", error.ToString(), StringComparison.Ordinal);
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
            InfraGate__Runtime__Environment=Development

            # Generic Approval Core
            InfraGate__Approval__Root=.mcp-approvals

            # Kubernetes Adapter
            InfraGate__Kubernetes__KubeConfig=.kube/mcp-nginx-demo.config
            InfraGate__Kubernetes__AllowedNamespaces__0=mcp-nginx-demo

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
    public async Task ExecuteAsync_GenerateProductionProfile_EmitsRequiredTokenIntrospectionSettings()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"infragate-production-{Guid.NewGuid():N}.env");
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            int exitCode = await RunProfileCli.ExecuteAsync(
                ["generate", "production", "--output", outputPath],
                output,
                error,
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Empty(error.ToString());
            string envFile = await File.ReadAllTextAsync(outputPath);
            Assert.Contains($"{RunProfileConventions.Env.TokenIntrospectionEnabled}=true", envFile, StringComparison.Ordinal);
            Assert.Contains($"{RunProfileConventions.Env.TokenIntrospectionClientId}=infra-gate-token-introspection", envFile, StringComparison.Ordinal);
            Assert.Contains(RunProfileConventions.Env.TokenIntrospectionClientSecret, envFile, StringComparison.Ordinal);
            Assert.Contains($"{RunProfileConventions.Env.MaxAcceptedAccessTokenLifetimeSeconds}=300", envFile, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
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
        Assert.Contains("InfraGate__Gateway__AspNetCoreUrls", keys);
        Assert.Contains("InfraGate__Gateway__DownstreamAssembly", keys);
        Assert.Contains("InfraGate__Gateway__GuardAuditRoot", keys);
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
        Assert.Contains("InfraGate__Auth__OAuthAuthority", keys);
        Assert.Contains("InfraGate__Auth__OAuthMetadataAddress", keys);
        Assert.Contains("InfraGate__Auth__OAuthResource", keys);
        Assert.Contains("InfraGate__Auth__OAuthScope", keys);
        Assert.Contains("InfraGate__Auth__OAuthRequireHttpsMetadata", keys);
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
        Assert.Contains("InfraGate__Approval__BaseUrl", keys);
        Assert.Contains("InfraGate__Auth__ApprovalOAuthClientId", keys);
        Assert.Contains("InfraGate__Auth__ApprovalOAuthCallbackPath", keys);
        Assert.Contains("InfraGate__Auth__ApprovalOAuthAuthorizationEndpoint", keys);
        Assert.Contains("InfraGate__Auth__ApprovalOAuthTokenEndpoint", keys);
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
        Assert.Contains("InfraGate__Gateway__AspNetCoreUrls=http://0.0.0.0:3001", content, StringComparison.Ordinal);
        Assert.Contains("InfraGate__Auth__OAuthScope=mcp:tools", content, StringComparison.Ordinal);
        Assert.Contains("InfraGate__Auth__OAuthAuthority=http://127.0.0.1:3010/realms/infra-gate", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithOpenRouterDefaults_InheritsDefaultApiKey()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            defaults:
              openRouter:
                apiKey: default-openrouter-key
            profiles:
              local-compose:
                kind: compose
                openRouter: {}
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
        Assert.Contains(
            $"{RunProfileConventions.Env.OpenRouterApiKey}=default-openrouter-key",
            content,
            StringComparison.Ordinal);
    }

    private static readonly HashSet<string> ComposeStackProfileKeys =
        new(StringComparer.Ordinal)
        {
            "InfraGate__Runtime__Environment",
            "InfraGate__Gateway__AspNetCoreUrls",
            "InfraGate__Gateway__DownstreamAssembly",
            "InfraGate__Gateway__DownstreamAssemblyHash",
            "InfraGate__Gateway__GuardAuditRoot",
            "InfraGate__Auth__OAuthAuthority",
            "InfraGate__Auth__OAuthMetadataAddress",
            "InfraGate__Auth__OAuthResource",
            "InfraGate__Auth__OAuthScope",
            "InfraGate__Auth__OAuthRequireHttpsMetadata",
            "InfraGate__Auth__TokenIntrospectionEnabled",
            "InfraGate__Auth__TokenIntrospectionEndpoint",
            "InfraGate__Auth__TokenIntrospectionClientId",
            "InfraGate__Auth__TokenIntrospectionClientSecret",
            "InfraGate__Auth__TokenIntrospectionCacheSeconds",
            "InfraGate__Auth__MaxAcceptedAccessTokenLifetimeSeconds",
            "InfraGate__Approval__BaseUrl",
            "InfraGate__Auth__ApprovalOAuthClientId",
            "InfraGate__Auth__ApprovalOAuthCallbackPath",
            "InfraGate__Auth__ApprovalOAuthAuthorizationEndpoint",
            "InfraGate__Auth__ApprovalOAuthTokenEndpoint",
            "InfraGate__Approval__Root",
            "InfraGate__Kubernetes__KubeConfig",
            "InfraGate__Kubernetes__AllowedNamespaces__0",
            "INFRA_GATE_BIND_ADDRESS",
            "INFRA_GATE_BIND_PORT",
            "INFRA_GATE_GATEWAY_IMAGE",
            "INFRA_GATE_KUBECONFIG_HOST_PATH",
            "INFRA_GATE_APPROVAL_HOST_PATH",
            "INFRA_GATE_GUARD_AUDIT_HOST_PATH",
            "INFRA_GATE_DATA_PROTECTION_HOST_PATH"
        };

    private static readonly HashSet<string> LocalComposeProfileKeys =
        new(StringComparer.Ordinal)
        {
            "InfraGate__Runtime__Environment",
            "InfraGate__Gateway__AspNetCoreUrls",
            "InfraGate__Gateway__DownstreamAssembly",
            "InfraGate__Gateway__GuardAuditRoot",
            "InfraGate__Auth__OAuthAuthority",
            "InfraGate__Auth__OAuthMetadataAddress",
            "InfraGate__Auth__OAuthResource",
            "InfraGate__Auth__OAuthScope",
            "InfraGate__Auth__OAuthRequireHttpsMetadata",
            "InfraGate__Approval__BaseUrl",
            "InfraGate__Auth__ApprovalOAuthClientId",
            "InfraGate__Auth__ApprovalOAuthCallbackPath",
            "InfraGate__Auth__ApprovalOAuthAuthorizationEndpoint",
            "InfraGate__Auth__ApprovalOAuthTokenEndpoint",
            "InfraGate__DownstreamAuth__Required",
            "InfraGate__DownstreamAuth__Authority",
            "InfraGate__DownstreamAuth__MetadataAddress",
            "InfraGate__DownstreamAuth__RequireHttpsMetadata",
            "InfraGate__DownstreamAuth__Audience",
            "InfraGate__DownstreamAuth__Scope",
            "InfraGate__DownstreamAuth__GatewayClientId",
            "InfraGate__DownstreamAuth__GatewayClientSecret",
            "InfraGate__Approval__Root",
            "InfraGate__Approval__Postgres__ConnectionString",
            "InfraGate__Approval__Postgres__RunMigrationsOnStartup",
            "InfraGate__Kubernetes__KubeConfig",
            "InfraGate__Kubernetes__AllowedNamespaces__0",
            "INFRA_GATE_BIND_ADDRESS",
            "INFRA_GATE_BIND_PORT",
            "INFRA_GATE_GATEWAY_IMAGE",
            "INFRA_GATE_KUBECONFIG_HOST_PATH",
            "INFRA_GATE_APPROVAL_HOST_PATH",
            "INFRA_GATE_GUARD_AUDIT_HOST_PATH",
            "INFRA_GATE_DATA_PROTECTION_HOST_PATH",
            "InfraGate__Observer__AspNetCoreUrls",
            "InfraGate__Observer__GatewayBaseUrl",
            "InfraGate__Observer__ClientCredentials__Authority",
            "InfraGate__Observer__ClientCredentials__ClientId",
            "InfraGate__Observer__ClientCredentials__ClientSecret",
            "InfraGate__Observer__ClientCredentials__Scope",
            "InfraGate__Observer__ClientCredentials__UseDPoP",
            "InfraGate__Observer__LlmProvider",
            "InfraGate__Observer__LlmModel",
            "InfraGate__Observer__CycleIntervalSeconds",
            "InfraGate__Observer__WallClockCapSeconds",
            "InfraGate__Observer__MaxToolIterations",
            "InfraGate__Observer__FileSinkRoot",
            "InfraGate__Observer__PlannerHandoffUrl",
            "INFRA_GATE_OBSERVER_HOST_PATH",
            "InfraGate__Observer__AllowedNamespaces__0",
            "InfraGate__Observer__AuditConnectionString",
            "InfraGate__Planner__AspNetCoreUrls",
            "InfraGate__Planner__GatewayBaseUrl",
            "InfraGate__Planner__ExecutorHandoffUrl",
            "InfraGate__Planner__ClientCredentials__ClientId",
            "InfraGate__Planner__ClientCredentials__ClientSecret",
            "InfraGate__Planner__ClientCredentials__Authority",
            "InfraGate__Planner__ClientCredentials__Scope",
            "InfraGate__Planner__ClientCredentials__UseDPoP",
            "InfraGate__Planner__LlmProvider",
            "InfraGate__Planner__LlmModel",
            "InfraGate__Planner__MaxToolIterations",
            "InfraGate__Planner__AnomalyWallClockCapSeconds",
            "InfraGate__Planner__FileSinkRoot",
            "INFRA_GATE_PLANNER_HOST_PATH",
            "InfraGate__Executor__AspNetCoreUrls",
            "InfraGate__Executor__GatewayBaseUrl",
            "InfraGate__Executor__ClientCredentials__ClientId",
            "InfraGate__Executor__ClientCredentials__ClientSecret",
            "InfraGate__Executor__ClientCredentials__Authority",
            "InfraGate__Executor__ClientCredentials__Scope",
            "InfraGate__Executor__ClientCredentials__UseDPoP"
        };

    private static readonly HashSet<string> SourceGatewayProfileKeys =
        new(StringComparer.Ordinal)
        {
            "InfraGate__Runtime__Environment",
            "InfraGate__Gateway__AspNetCoreUrls",
            "InfraGate__Gateway__DownstreamAssembly",
            "InfraGate__Gateway__GuardAuditRoot",
            "InfraGate__Auth__OAuthAuthority",
            "InfraGate__Auth__OAuthMetadataAddress",
            "InfraGate__Auth__OAuthResource",
            "InfraGate__Auth__OAuthScope",
            "InfraGate__Auth__OAuthRequireHttpsMetadata",
            "InfraGate__Approval__BaseUrl",
            "InfraGate__Auth__ApprovalOAuthClientId",
            "InfraGate__Auth__ApprovalOAuthCallbackPath",
            "InfraGate__Auth__ApprovalOAuthAuthorizationEndpoint",
            "InfraGate__Auth__ApprovalOAuthTokenEndpoint",
            "InfraGate__Approval__Root",
            "InfraGate__DownstreamAuth__Required",
            "InfraGate__Kubernetes__KubeConfig",
            "InfraGate__Kubernetes__AllowedNamespaces__0",
            "InfraGate__Observer__AspNetCoreUrls",
            "InfraGate__Observer__GatewayBaseUrl",
            "InfraGate__Observer__ClientCredentials__Authority",
            "InfraGate__Observer__ClientCredentials__ClientId",
            "InfraGate__Observer__ClientCredentials__ClientSecret",
            "InfraGate__Observer__ClientCredentials__Scope",
            "InfraGate__Observer__ClientCredentials__UseDPoP",
            "InfraGate__Observer__LlmProvider",
            "InfraGate__Observer__LlmModel",
            "InfraGate__Observer__CycleIntervalSeconds",
            "InfraGate__Observer__WallClockCapSeconds",
            "InfraGate__Observer__MaxToolIterations",
            "InfraGate__Observer__FileSinkRoot",
            "InfraGate__Observer__PlannerHandoffUrl",
            "InfraGate__Observer__AllowedNamespaces__0",
            "InfraGate__Planner__AspNetCoreUrls",
            "InfraGate__Planner__GatewayBaseUrl",
            "InfraGate__Planner__ExecutorHandoffUrl",
            "InfraGate__Planner__ClientCredentials__ClientId",
            "InfraGate__Planner__ClientCredentials__ClientSecret",
            "InfraGate__Planner__ClientCredentials__Authority",
            "InfraGate__Planner__ClientCredentials__Scope",
            "InfraGate__Planner__ClientCredentials__UseDPoP",
            "InfraGate__Planner__LlmProvider",
            "InfraGate__Planner__LlmModel",
            "InfraGate__Planner__MaxToolIterations",
            "InfraGate__Planner__AnomalyWallClockCapSeconds",
            "InfraGate__Planner__FileSinkRoot",
            "InfraGate__Executor__AspNetCoreUrls",
            "InfraGate__Executor__GatewayBaseUrl",
            "InfraGate__Executor__ClientCredentials__ClientId",
            "InfraGate__Executor__ClientCredentials__ClientSecret",
            "InfraGate__Executor__ClientCredentials__Authority",
            "InfraGate__Executor__ClientCredentials__Scope",
            "InfraGate__Executor__ClientCredentials__UseDPoP"
        };

    private static readonly HashSet<string> SmokeProfileKeys =
        new(StringComparer.Ordinal)
        {
            "InfraGate__Runtime__Environment",
            "InfraGate__Gateway__AspNetCoreUrls",
            "InfraGate__Gateway__DownstreamAssembly",
            "InfraGate__Gateway__GuardAuditRoot",
            "InfraGate__Auth__OAuthAuthority",
            "InfraGate__Auth__OAuthMetadataAddress",
            "InfraGate__Auth__OAuthResource",
            "InfraGate__Auth__OAuthScope",
            "InfraGate__Auth__OAuthRequireHttpsMetadata",
            "InfraGate__Approval__BaseUrl",
            "InfraGate__Auth__ApprovalOAuthClientId",
            "InfraGate__Auth__ApprovalOAuthCallbackPath",
            "InfraGate__Auth__ApprovalOAuthAuthorizationEndpoint",
            "InfraGate__Auth__ApprovalOAuthTokenEndpoint",
            "InfraGate__DownstreamAuth__Required",
            "InfraGate__DownstreamAuth__Authority",
            "InfraGate__DownstreamAuth__MetadataAddress",
            "InfraGate__DownstreamAuth__RequireHttpsMetadata",
            "InfraGate__DownstreamAuth__Audience",
            "InfraGate__DownstreamAuth__Scope",
            "InfraGate__DownstreamAuth__GatewayClientId",
            "InfraGate__DownstreamAuth__GatewayClientSecret",
            "InfraGate__Approval__Root",
            "InfraGate__Approval__Postgres__ConnectionString",
            "InfraGate__Approval__Postgres__RunMigrationsOnStartup",
            "InfraGate__Kubernetes__KubeConfig",
            "InfraGate__Kubernetes__AllowedNamespaces__0",
            "INFRA_GATE_BIND_ADDRESS",
            "INFRA_GATE_BIND_PORT",
            "INFRA_GATE_GATEWAY_IMAGE",
            "INFRA_GATE_KUBECONFIG_HOST_PATH",
            "INFRA_GATE_APPROVAL_HOST_PATH",
            "INFRA_GATE_GUARD_AUDIT_HOST_PATH",
            "INFRA_GATE_DATA_PROTECTION_HOST_PATH"
        };

    private static readonly HashSet<string> DevelopmentProfileKeys =
        new(StringComparer.Ordinal)
        {
            "InfraGate__Runtime__Environment",
            "InfraGate__Gateway__AspNetCoreUrls",
            "InfraGate__Gateway__DownstreamAssembly",
            "InfraGate__Gateway__GuardAuditRoot",
            "InfraGate__Auth__OAuthAuthority",
            "InfraGate__Auth__OAuthMetadataAddress",
            "InfraGate__Auth__OAuthResource",
            "InfraGate__Auth__OAuthScope",
            "InfraGate__Auth__OAuthRequireHttpsMetadata",
            "InfraGate__Approval__BaseUrl",
            "InfraGate__Auth__ApprovalOAuthClientId",
            "InfraGate__Auth__ApprovalOAuthCallbackPath",
            "InfraGate__Auth__ApprovalOAuthAuthorizationEndpoint",
            "InfraGate__Auth__ApprovalOAuthTokenEndpoint",
            "InfraGate__Approval__Root",
            "InfraGate__Approval__Postgres__ConnectionString",
            "InfraGate__Approval__Postgres__RunMigrationsOnStartup",
            "InfraGate__DownstreamAuth__Required",
            "InfraGate__Kubernetes__KubeConfig",
            "InfraGate__Kubernetes__AllowedNamespaces__0",
            "INFRA_GATE_BIND_ADDRESS",
            "INFRA_GATE_BIND_PORT",
            "INFRA_GATE_GATEWAY_IMAGE",
            "INFRA_GATE_KUBECONFIG_HOST_PATH",
            "INFRA_GATE_APPROVAL_HOST_PATH",
            "INFRA_GATE_GUARD_AUDIT_HOST_PATH",
            "INFRA_GATE_DATA_PROTECTION_HOST_PATH",
            "InfraGate__AgentGuardrails__ModelVisibleContent__Enabled",
            "InfraGate__AgentGuardrails__ModelVisibleContent__SemanticClassifierEnabled",
            "InfraGate__AgentGuardrails__ModelVisibleContent__RequestTimeoutMilliseconds",
            "InfraGate__AgentGuardrails__ModelVisibleContent__MaximumInputCharacters",
            "InfraGate__AgentGuardrails__ModelVisibleContent__UnavailableBehavior"
        };

    private static readonly HashSet<string> MinimalProfileKeys =
        new(StringComparer.Ordinal)
        {
            "InfraGate__Runtime__Environment",
            "InfraGate__Approval__Root",
            "InfraGate__DownstreamAuth__Required",
            "InfraGate__Kubernetes__KubeConfig",
            "InfraGate__Kubernetes__AllowedNamespaces__0"
        };

    public static TheoryData<string, HashSet<string>> ProfileKeySetData = new()
    {
        { "local-compose", LocalComposeProfileKeys },
        { "local-source-gateway", SourceGatewayProfileKeys },
        { "development", DevelopmentProfileKeys },
        { "production", ComposeStackProfileKeys },
        { "test-integration", MinimalProfileKeys },
        { "test-gateway-integration", MinimalProfileKeys },
        { "test-safety-e2e", MinimalProfileKeys },
        { "smoke-local", SmokeProfileKeys },
        { "smoke-release", SmokeProfileKeys }
    };

    [Theory]
    [MemberData(nameof(ProfileKeySetData))] // NOSONAR — HashSet<string> is not serializable; fine for local test execution
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
    public async Task ExecuteAsync_GenerateWithSetDownstreamAssemblyHash_OverridesValue()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                gateway:
                  downstreamAssembly: /app/server/InfraGate.McpServer.dll
                  downstreamAssemblyHash: original-hash00000000000000000000000000000000000000000000000000000000
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
             "--set", "gateway.downstreamAssemblyHash=override-hash0000000000000000000000000000000000000000000000000000000"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAssemblyHash}=override-hash0000000000000000000000000000000000000000000000000000000", content, StringComparison.Ordinal);
        Assert.DoesNotContain("original-hash", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetAgentHostPaths_OverridesValues()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                observer:
                  observerHostPath: .observer/original
                planner:
                  plannerHostPath: .planner/original
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
             "--set", "observer.observerHostPath=/tmp/observer/findings",
             "--set", "planner.plannerHostPath=/tmp/planner/proposals"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("INFRA_GATE_OBSERVER_HOST_PATH=/tmp/observer/findings", content, StringComparison.Ordinal);
        Assert.Contains("INFRA_GATE_PLANNER_HOST_PATH=/tmp/planner/proposals", content, StringComparison.Ordinal);
        Assert.DoesNotContain("INFRA_GATE_OBSERVER_HOST_PATH=.observer/original", content, StringComparison.Ordinal);
        Assert.DoesNotContain("INFRA_GATE_PLANNER_HOST_PATH=.planner/original", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetAgentFields_OverridesValues()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                observer: {}
                planner: {}
                executor: {}
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
            [
                "generate", "local-compose", "--config", configPath, "--output", outputPath,
                "--set", "observer.aspnetcoreUrls=http://127.0.0.1:3102",
                "--set", "observer.gatewayBaseUrl=http://gateway/mcp",
                "--set", "observer.oauthAuthority=http://keycloak/realms/infra-gate",
                "--set", "observer.clientId=observer-client",
                "--set", "observer.clientSecret=observer-secret",
                "--set", "observer.scope=mcp:tools.readonly",
                "--set", "observer.llmProvider=openai",
                "--set", "observer.llmModel=gpt-test",
                "--set", "observer.cycleCadenceSeconds=30",
                "--set", "observer.cycleWallClockCapSeconds=20",
                "--set", "observer.maxToolIterations=5",
                "--set", "observer.fileSinkRoot=/observer/out",
                "--set", "observer.plannerHandoffUrl=http://planner/handoff",
                "--set", "observer.observerHostPath=/observer/state",
                "--set", "planner.aspnetcoreUrls=http://127.0.0.1:3103",
                "--set", "planner.gatewayBaseUrl=http://gateway/mcp",
                "--set", "planner.executorHandoffUrl=http://executor/handoff",
                "--set", "planner.clientId=planner-client",
                "--set", "planner.clientSecret=planner-secret",
                "--set", "planner.oauthAuthority=http://keycloak/realms/infra-gate",
                "--set", "planner.scope=mcp:tools.propose",
                "--set", "planner.llmProvider=openai",
                "--set", "planner.llmModel=gpt-planner",
                "--set", "openRouter.apiKey=openrouter-key",
                "--set", "planner.anomalyWallClockCapSeconds=15",
                "--set", "planner.batchWallClockCapSeconds=45",
                "--set", "planner.maxToolIterations=8",
                "--set", "planner.fileSinkRoot=/planner/out",
                "--set", "planner.plannerHostPath=/planner/state",
                "--set", "executor.aspnetcoreUrls=http://127.0.0.1:3104",
                "--set", "executor.gatewayBaseUrl=http://gateway/mcp",
                "--set", "executor.clientId=executor-client",
                "--set", "executor.clientSecret=executor-secret",
                "--set", "executor.oauthAuthority=http://keycloak/realms/infra-gate",
                "--set", "executor.scope=mcp:tools.execute",
                "--set", "executor.concurrencyCap=2",
                "--set", "executor.watchTimeoutSeconds=120",
                "--set", "executor.executorHostPath=/executor/state"
            ],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Dictionary<string, string> expectedAssignments = new(StringComparer.Ordinal)
        {
            [RunProfileConventions.Env.ObserverAspnetcoreUrls] = "http://127.0.0.1:3102",
            [RunProfileConventions.Env.ObserverGatewayBaseUrl] = "http://gateway/mcp",
            [RunProfileConventions.Env.ObserverOAuthAuthority] = "http://keycloak/realms/infra-gate",
            [RunProfileConventions.Env.ObserverClientId] = "observer-client",
            [RunProfileConventions.Env.ObserverClientSecret] = "observer-secret",
            [RunProfileConventions.Env.ObserverScope] = "mcp:tools.readonly",
            [RunProfileConventions.Env.ObserverLlmProvider] = "openai",
            [RunProfileConventions.Env.ObserverLlmModel] = "gpt-test",
            [RunProfileConventions.Env.OpenRouterApiKey] = "openrouter-key",
            [RunProfileConventions.Env.ObserverCycleIntervalSeconds] = "30",
            [RunProfileConventions.Env.ObserverCycleWallClockCapSeconds] = "20",
            [RunProfileConventions.Env.ObserverMaxToolIterations] = "5",
            [RunProfileConventions.Env.ObserverFileSinkRoot] = "/observer/out",
            [RunProfileConventions.Env.ObserverPlannerHandoffUrl] = "http://planner/handoff",
            [RunProfileConventions.Env.ObserverHostPath] = "/observer/state",
            [RunProfileConventions.Env.PlannerAspnetcoreUrls] = "http://127.0.0.1:3103",
            [RunProfileConventions.Env.PlannerGatewayBaseUrl] = "http://gateway/mcp",
            [RunProfileConventions.Env.PlannerExecutorHandoffUrl] = "http://executor/handoff",
            [RunProfileConventions.Env.PlannerClientId] = "planner-client",
            [RunProfileConventions.Env.PlannerClientSecret] = "planner-secret",
            [RunProfileConventions.Env.PlannerOAuthAuthority] = "http://keycloak/realms/infra-gate",
            [RunProfileConventions.Env.PlannerOAuthScope] = "mcp:tools.propose",
            [RunProfileConventions.Env.PlannerLlmProvider] = "openai",
            [RunProfileConventions.Env.PlannerLlmModel] = "gpt-planner",
            [RunProfileConventions.Env.PlannerAnomalyWallClockCapSeconds] = "15",
            [RunProfileConventions.Env.PlannerBatchWallClockCapSeconds] = "45",
            [RunProfileConventions.Env.PlannerMaxToolIterations] = "8",
            [RunProfileConventions.Env.PlannerFileSinkRoot] = "/planner/out",
            [RunProfileConventions.Env.PlannerHostPath] = "/planner/state",
            [RunProfileConventions.Env.ExecutorAspnetcoreUrls] = "http://127.0.0.1:3104",
            [RunProfileConventions.Env.ExecutorGatewayBaseUrl] = "http://gateway/mcp",
            [RunProfileConventions.Env.ExecutorClientId] = "executor-client",
            [RunProfileConventions.Env.ExecutorClientSecret] = "executor-secret",
            [RunProfileConventions.Env.ExecutorOAuthAuthority] = "http://keycloak/realms/infra-gate",
            [RunProfileConventions.Env.ExecutorOAuthScope] = "mcp:tools.execute",
            [RunProfileConventions.Env.ExecutorConcurrencyCap] = "2",
            [RunProfileConventions.Env.ExecutorWatchTimeoutSeconds] = "120",
            [RunProfileConventions.Env.ExecutorHostPath] = "/executor/state"
        };

        foreach (KeyValuePair<string, string> expectedAssignment in expectedAssignments)
        {
            Assert.Contains(
                $"{expectedAssignment.Key}={expectedAssignment.Value}",
                content,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetAllSections_OverridesInfrastructureFields()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                gateway: {}
                identityProvider: {}
                approvalAuthority: {}
                genericApprovalCore:
                  approvalRoot: /data/approvals
                downstreamAuth: {}
                host: {}
                observer: {}
                planner: {}
                executor: {}
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
            [
                "generate", "local-compose", "--config", configPath, "--output", outputPath,
                "--set", "gateway.aspnetcoreUrls=http://localhost:3001",
                "--set", "gateway.downstreamAssembly=InfraGate.McpServer.dll",
                "--set", "gateway.guardAuditRoot=/audit",
                "--set", "identityProvider.authority=http://auth:8080/realms/test",
                "--set", "identityProvider.metadataAddress=http://auth:8080/realms/test/.well-known/openid-configuration",
                "--set", "identityProvider.resource=gateway",
                "--set", "identityProvider.scope=openid",
                "--set", "identityProvider.requireHttpsMetadata=false",
                "--set", "approvalAuthority.baseUrl=http://gateway.test",
                "--set", "approvalAuthority.oauthClientId=approval-client",
                "--set", "approvalAuthority.oauthCallbackPath=/approval/callback",
                "--set", "approvalAuthority.oauthAuthorizationEndpoint=http://auth/authorize",
                "--set", "approvalAuthority.oauthTokenEndpoint=http://auth/token",
                "--set", "genericApprovalCore.approvalRoot=/data/approvals",
                "--set", "genericApprovalCore.postgresConnectionString=Host=db;Database=approvals",
                "--set", "genericApprovalCore.runMigrationsOnStartup=true",
                "--set", "downstreamAuth.required=true",
                "--set", "downstreamAuth.authority=http://auth:8080",
                "--set", "downstreamAuth.metadataAddress=http://auth:8080/.well-known/oidc",
                "--set", "downstreamAuth.requireHttpsMetadata=false",
                "--set", "downstreamAuth.audience=http://localhost:3001",
                "--set", "downstreamAuth.scope=mcp:tools",
                "--set", "downstreamAuth.gatewayClientId=gateway-client",
                "--set", "downstreamAuth.gatewayClientSecret=gateway-secret",
                "--set", "host.bindAddress=0.0.0.0",
                "--set", "host.bindPort=8080",
                "--set", "host.gatewayImage=infragate/gateway:latest",
                "--set", "host.kubeconfigHostPath=/host/kubeconfig",
                "--set", "host.approvalHostPath=/host/approvals",
                "--set", "host.guardAuditHostPath=/host/audit",
                "--set", "host.dataProtectionHostPath=/host/dataprotection",
                "--set", "observer.auditConnectionString=Host=audit;Database=observer_audit",
            ],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains($"{RunProfileConventions.Env.AspnetcoreUrls}=http://localhost:3001", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAssembly}=InfraGate.McpServer.dll", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.GuardAuditRoot}=/audit", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.OauthAuthority}=http://auth:8080/realms/test", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.OauthMetadataAddress}=http://auth:8080/realms/test/.well-known/openid-configuration", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.OauthResource}=gateway", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.OauthScope}=openid", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.OauthRequireHttpsMetadata}=false", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ApprovalBaseUrl}=http://gateway.test", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ApprovalOauthClientId}=approval-client", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ApprovalOauthCallbackPath}=/approval/callback", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ApprovalOauthAuthorizationEndpoint}=http://auth/authorize", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ApprovalOauthTokenEndpoint}=http://auth/token", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ApprovalRoot}=/data/approvals", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAuthRequired}=true", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAuthAuthority}=http://auth:8080", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAuthMetadataAddress}=http://auth:8080/.well-known/oidc", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAuthRequireHttpsMetadata}=false", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAuthAudience}=http://localhost:3001", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAuthScope}=mcp:tools", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAuthGatewayClientId}=gateway-client", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DownstreamAuthGatewayClientSecret}=gateway-secret", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.BindAddress}=0.0.0.0", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.BindPort}=8080", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.GatewayImage}=infragate/gateway:latest", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.KubeconfigHostPath}=/host/kubeconfig", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ApprovalHostPath}=/host/approvals", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.GuardAuditHostPath}=/host/audit", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.DataProtectionHostPath}=/host/dataprotection", content, StringComparison.Ordinal);
        Assert.Contains($"{RunProfileConventions.Env.ObserverAuditConnectionString}=Host=audit;Database=observer_audit", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("observer.unknownField=value")]
    [InlineData("planner.unknownField=value")]
    [InlineData("executor.unknownField=value")]
    public async Task ExecuteAsync_GenerateWithSetUnknownAgentField_ReturnsError(string overrideArgument)
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                observer: {}
                planner: {}
                executor: {}
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
            ["generate", "local-compose", "--config", configPath, "--output", outputPath, "--set", overrideArgument],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        string overridePath = overrideArgument[..overrideArgument.IndexOf('=', StringComparison.Ordinal)];
        Assert.Contains(overridePath, error.ToString(), StringComparison.Ordinal);
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
        Assert.Contains("InfraGate__Auth__OAuthAuthority=http://172.17.0.1:3010/realms/infra-gate", content, StringComparison.Ordinal);
        Assert.Contains("InfraGate__Auth__OAuthMetadataAddress=http://172.17.0.1:3010/realms/infra-gate/.well-known/openid-configuration", content, StringComparison.Ordinal);
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
    public async Task ExecuteAsync_GenerateWithSetUnknownOpenRouterField_ReturnsError()
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
             "--set", "openRouter.unknownField=value"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("openRouter.unknownField", error.ToString(), StringComparison.Ordinal);
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
        Assert.Contains("InfraGate__Gateway__AspNetCoreUrls=http://0.0.0.0:9090", content, StringComparison.Ordinal);
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
        Assert.Contains("InfraGate__Approval__BaseUrl=http://0.0.0.0:4000", content, StringComparison.Ordinal);
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
        Assert.Contains("InfraGate__Approval__Root=/custom/path", content, StringComparison.Ordinal);
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
    public async Task ExecuteAsync_NoArgs_ReturnsError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            [],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Command is required.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCommand_ReturnsError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["foobar"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command: foobar", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ValidateWithValidConfig_ReturnsSuccess()
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
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["validate", "--config", configPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("Run profile configuration is valid.", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ConfigReadFailure_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            {} # not a valid run profile document
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["list", "--config", configPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.NotEmpty(error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetWithoutValue_ReturnsError()
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
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath, "--set"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--set", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetPathWithoutDot_ReturnsError()
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
             "--set", "gateway=value"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown --set path: gateway", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateAppSettingsWithExistingForeignFile_ReturnsRemovedFormatError()
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
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "removed-format-output.json");
        await File.WriteAllTextAsync(outputPath, """{"not":"generated"}""");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--format", "appsettings", "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format is no longer supported", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateAppSettingsWithMatchingGeneratedFile_ReturnsRemovedFormatError()
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
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "removed-format-generated-output.json");
        await File.WriteAllTextAsync(outputPath,
            """{"_generated":{"source":"run-profiles.yaml","profile":"local-compose"},"InfraGate":{"OldKey":"old"}}""");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--format", "appsettings", "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format is no longer supported", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithFormatWithoutValue_ReturnsError()
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
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath, "--format"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithUnknownFormat_ReturnsError()
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
            ["generate", "local-stdio", "--config", configPath, "--format", "json", "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format", error.ToString(), StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task ExecuteAsync_GenerateWithDownstreamAuthSection_EmitsDownstreamAuthEnvVars()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                downstreamAuth:
                  required: "true"
                  authority: http://127.0.0.1:3010/realms/infra-gate
                  metadataAddress: http://keycloak:8080/realms/infra-gate/.well-known/openid-configuration
                  requireHttpsMetadata: "false"
                  audience: urn:infra-gate:mcp-server
                  scope: mcp:downstream
                  gatewayClientId: infra-gate-gateway-service
                  gatewayClientSecret: gateway-service-secret
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
        Assert.Contains("InfraGate__DownstreamAuth__Required", keys);
        Assert.Contains("InfraGate__DownstreamAuth__Authority", keys);
        Assert.Contains("InfraGate__DownstreamAuth__MetadataAddress", keys);
        Assert.Contains("InfraGate__DownstreamAuth__RequireHttpsMetadata", keys);
        Assert.Contains("InfraGate__DownstreamAuth__Audience", keys);
        Assert.Contains("InfraGate__DownstreamAuth__Scope", keys);
        Assert.Contains("InfraGate__DownstreamAuth__GatewayClientId", keys);
        Assert.Contains("InfraGate__DownstreamAuth__GatewayClientSecret", keys);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithoutProfileName_ReturnsError()
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
            """);
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Profile name is required.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithMalformedAppSettingsFile_ReturnsRemovedFormatError()
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
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "removed-format-malformed-output.json");
        await File.WriteAllTextAsync(outputPath, "not-valid-json{{{");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-compose", "--config", configPath, "--format", "appsettings", "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format is no longer supported", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithMalformedEnvFile_ReturnsErrorWithoutForce()
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
        string outputPath = Path.Combine(Path.GetDirectoryName(configPath)!, "out.env");
        await File.WriteAllTextAsync(outputPath, "# Some unrelated env file\nKEY=value\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await RunProfileCli.ExecuteAsync(
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Will not overwrite", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetDownstreamAuthField_OverridesValue()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                downstreamAuth:
                  required: "true"
                  authority: http://original/auth
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
             "--set", "downstreamAuth.audience=urn:custom-audience"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("InfraGate__DownstreamAuth__Audience=urn:custom-audience", content, StringComparison.Ordinal);
        Assert.Contains("InfraGate__DownstreamAuth__Authority=http://original/auth", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetIdentityProviderField_OverridesValue()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                identityProvider:
                  authority: http://127.0.0.1:3010/realms/original
                  metadataAddress: http://original/metadata
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
             "--set", "identityProvider.realmImport=custom-realm.json"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("InfraGate__Auth__OAuthAuthority=http://127.0.0.1:3010/realms/original", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetUnknownDownstreamAuthField_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                downstreamAuth:
                  required: "true"
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
             "--set", "downstreamAuth.unknownField=value"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("downstreamAuth.unknownField", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetUnknownGatewayField_ReturnsError()
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
             "--set", "gateway.unknownField=value"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("gateway.unknownField", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetUnknownHostField_ReturnsError()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                host:
                  bindAddress: 127.0.0.1
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
             "--set", "host.unknownField=value"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("host.unknownField", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithDownstreamAuthWithoutClientSecret_DoesNotEmitClientSecretKey()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-stdio:
                kind: mcp-stdio
                downstreamAuth:
                  required: "true"
                  authority: http://127.0.0.1:3010/realms/infra-gate
                  requireHttpsMetadata: "false"
                  audience: urn:infra-gate:mcp-server
                  scope: mcp:downstream
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
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        HashSet<string> keys = ParseEnvKeys(await File.ReadAllTextAsync(outputPath));
        Assert.Contains("InfraGate__DownstreamAuth__Required", keys);
        Assert.Contains("InfraGate__DownstreamAuth__Authority", keys);
        Assert.DoesNotContain("InfraGate__DownstreamAuth__GatewayClientId", keys);
        Assert.DoesNotContain("InfraGate__DownstreamAuth__GatewayClientSecret", keys);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateEnvWithPostgresConnectionString_EmitsConnectionStringEnvVar()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                genericApprovalCore:
                  approvalRoot: /data/approvals
                  postgresConnectionString: "Host=postgres;Port=5432;Database=approvals;Username=app;Password=secret"
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
        Assert.Contains(
            $"{RunProfileConventions.Env.ApprovalPostgresConnectionString}=Host=postgres;Port=5432;Database=approvals;Username=app;Password=secret",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateEnvWithPostgresConnectionString_EmitsConnectionStringEnvVarForStdio()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-stdio:
                kind: mcp-stdio
                genericApprovalCore:
                  approvalRoot: .mcp-approvals
                  postgresConnectionString: "Host=postgres;Port=5432;Database=approvals;Username=app;Password=secret"
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
            ["generate", "local-stdio", "--config", configPath, "--output", outputPath],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains(
            $"{RunProfileConventions.Env.ApprovalPostgresConnectionString}=Host=postgres;Port=5432;Database=approvals;Username=app;Password=secret",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateEnv_DefaultsPostgresConnectionStringMergedIntoProfile()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            defaults:
              genericApprovalCore:
                approvalRoot: /data/approvals
                postgresConnectionString: "Host=postgres;Port=5432;Database=approvals;Username=app;Password=secret"
            profiles:
              local-compose:
                kind: compose
                genericApprovalCore:
                  approvalRoot: /custom/approvals
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
        Assert.Contains($"{RunProfileConventions.Env.ApprovalRoot}=/custom/approvals", content, StringComparison.Ordinal);
        Assert.Contains(
            $"{RunProfileConventions.Env.ApprovalPostgresConnectionString}=Host=postgres;Port=5432;Database=approvals;Username=app;Password=secret",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GenerateWithSetPostgresConnectionString_OverridesValue()
    {
        string configPath = await WriteConfigAsync(
            """
            version: 1
            profiles:
              local-compose:
                kind: compose
                genericApprovalCore:
                  approvalRoot: /data/approvals
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
             "--set", "genericApprovalCore.postgresConnectionString=Host=custom-pg;Port=5432"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        string content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains(
            $"{RunProfileConventions.Env.ApprovalPostgresConnectionString}=Host=custom-pg;Port=5432",
            content,
            StringComparison.Ordinal);
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
