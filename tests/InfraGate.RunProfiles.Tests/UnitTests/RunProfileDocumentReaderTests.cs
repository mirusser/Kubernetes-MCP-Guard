namespace InfraGate.RunProfiles.Tests.UnitTests;

public sealed class RunProfileDocumentReaderTests : IDisposable
{
    private readonly List<string> tempFiles = [];

    public void Dispose()
    {
        foreach (string f in tempFiles)
        {
            if (File.Exists(f))
                File.Delete(f);
        }
    }

    private string WriteYaml(string content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        tempFiles.Add(path);
        return path;
    }

    private const string MinimalValidYaml = """
        version: 1
        profiles:
          test-profile:
            kind: mcp-stdio
            domainAdapters:
              - name: myAdapter
                type: kubernetes
                kubernetes:
                  kubeconfig: /path/to/kubeconfig
                  allowedNamespaces:
                    - default
        """;

    [Fact]
    public async Task ReadAsync_MinimalValidYaml_ReturnsDocumentWithProfile()
    {
        string path = WriteYaml(MinimalValidYaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        Assert.Single(doc.Profiles);
        Assert.Equal("test-profile", doc.Profiles[0].Name);
    }

    [Fact]
    public async Task ReadAsync_MinimalValidYaml_ProfileKindIsPopulated()
    {
        string path = WriteYaml(MinimalValidYaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        Assert.Equal("mcp-stdio", doc.Profiles[0].Kind);
    }

    [Fact]
    public async Task ReadAsync_MinimalValidYaml_DomainAdapterIsPopulated()
    {
        string path = WriteYaml(MinimalValidYaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        var adapter = doc.Profiles[0].DomainAdapters[0];
        Assert.Equal("myAdapter", adapter.Name);
        Assert.Equal("kubernetes", adapter.Type);
        Assert.Equal("/path/to/kubeconfig", adapter.Kubernetes?.KubeConfig);
    }

    [Fact]
    public async Task ReadAsync_MinimalValidYaml_AllowedNamespacesPopulated()
    {
        string path = WriteYaml(MinimalValidYaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        var kubernetes = doc.Profiles[0].DomainAdapters[0].Kubernetes;
        Assert.NotNull(kubernetes);
        Assert.Contains("default", kubernetes.AllowedNamespaces);
    }

    [Fact]
    public async Task ReadAsync_NullPath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RunProfileDocumentReader.ReadAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_EmptyPath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            RunProfileDocumentReader.ReadAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_DuplicateProfileName_ThrowsInvalidOperationExceptionWithMessage()
    {
        string yaml = """
            version: 1
            profiles:
              my-profile:
                kind: mcp-stdio
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
              my-profile:
                kind: mcp-stdio
                domainAdapters:
                  - name: b
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProfileDocumentReader.ReadAsync(path, CancellationToken.None));

        Assert.Contains("my-profile", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_EmptyDocument_ThrowsInvalidOperationException()
    {
        string path = WriteYaml("---");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProfileDocumentReader.ReadAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_UnknownRootKey_ThrowsInvalidOperationException()
    {
        string yaml = """
            version: 1
            unknownKey: surprise
            profiles:
              p:
                kind: mcp-stdio
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProfileDocumentReader.ReadAsync(path, CancellationToken.None));

        Assert.Contains("Unknown YAML key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_MissingProfilesKey_ThrowsInvalidOperationException()
    {
        string yaml = "version: 1\n";
        string path = WriteYaml(yaml);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProfileDocumentReader.ReadAsync(path, CancellationToken.None));

        Assert.Contains("profiles", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_ProfileMissingKind_ThrowsInvalidOperationException()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProfileDocumentReader.ReadAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_UnsupportedDomainAdapterType_ThrowsInvalidOperationException()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                kind: mcp-stdio
                domainAdapters:
                  - name: a
                    type: unsupported-adapter
            """;
        string path = WriteYaml(yaml);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProfileDocumentReader.ReadAsync(path, CancellationToken.None));

        Assert.Contains("unsupported-adapter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_ZeroDomainAdapters_ThrowsInvalidOperationException()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                kind: mcp-stdio
                domainAdapters: []
            """;
        string path = WriteYaml(yaml);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProfileDocumentReader.ReadAsync(path, CancellationToken.None));

        Assert.Contains("exactly one", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_MultipleDomainAdapters_ThrowsInvalidOperationException()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                kind: mcp-stdio
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
                  - name: b
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k2
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProfileDocumentReader.ReadAsync(path, CancellationToken.None));

        Assert.Contains("exactly one", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_UnknownProfileKey_ThrowsInvalidOperationException()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                kind: mcp-stdio
                unknownProfileKey: oops
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunProfileDocumentReader.ReadAsync(path, CancellationToken.None));

        Assert.Contains("Unknown YAML key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_WithDefaults_DefaultsAreParsed()
    {
        string yaml = """
            version: 1
            defaults:
              gateway:
                aspnetcoreUrls: http://0.0.0.0:3001
            profiles:
              p:
                kind: mcp-stdio
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        Assert.NotNull(doc.Defaults);
        Assert.Equal("http://0.0.0.0:3001", doc.Defaults.Gateway?.AspnetcoreUrls);
    }

    [Fact]
    public async Task ReadAsync_WithObserverAllowedNamespaces_ParsesAsList()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                kind: mcp-stdio
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
                observer:
                  allowedNamespaces:
                    - ns1
                    - ns2
            """;
        string path = WriteYaml(yaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        var namespaces = doc.Profiles[0].Observer?.AllowedNamespaces;
        Assert.NotNull(namespaces);
        Assert.Contains("ns1", namespaces);
        Assert.Contains("ns2", namespaces);
    }

    [Fact]
    public async Task ReadAsync_WithGatewayDownstreamAssemblyHash_ParsesHash()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                kind: compose
                gateway:
                  downstreamAssembly: /app/server/InfraGate.McpServer.dll
                  downstreamAssemblyHash: a3e5f8c9d2b1e4076f5a3c8e1d0b9a2c7f4e6d5b8c3a1f0e9d7b6c5a4f3e2d1b0
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        Assert.Equal("/app/server/InfraGate.McpServer.dll", doc.Profiles[0].Gateway?.DownstreamAssembly);
        Assert.Equal("a3e5f8c9d2b1e4076f5a3c8e1d0b9a2c7f4e6d5b8c3a1f0e9d7b6c5a4f3e2d1b0", doc.Profiles[0].Gateway?.DownstreamAssemblyHash);
    }

    [Fact]
    public async Task ReadAsync_WithRuntimeMode_RuntimeModeIsPopulated()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                kind: compose
                runtimeMode: Development
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        Assert.Equal("Development", doc.Profiles[0].RuntimeMode);
    }

    [Fact]
    public async Task ReadAsync_WithIdentityProviderIntrospection_ParsesSettings()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                kind: compose
                identityProvider:
                  authority: https://issuer.example.com/realms/infra-gate
                  tokenIntrospectionEnabled: "true"
                  tokenIntrospectionEndpoint: https://issuer.example.com/realms/infra-gate/protocol/openid-connect/token/introspect
                  tokenIntrospectionClientId: infra-gate-token-introspection
                  tokenIntrospectionClientSecret: secret-placeholder
                  tokenIntrospectionCacheSeconds: "15"
                  maxAcceptedAccessTokenLifetimeSeconds: "300"
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        var identityProvider = doc.Profiles[0].IdentityProvider;
        Assert.NotNull(identityProvider);
        Assert.Equal("true", identityProvider.TokenIntrospectionEnabled);
        Assert.Equal("https://issuer.example.com/realms/infra-gate/protocol/openid-connect/token/introspect", identityProvider.TokenIntrospectionEndpoint);
        Assert.Equal("infra-gate-token-introspection", identityProvider.TokenIntrospectionClientId);
        Assert.Equal("secret-placeholder", identityProvider.TokenIntrospectionClientSecret);
        Assert.Equal("15", identityProvider.TokenIntrospectionCacheSeconds);
        Assert.Equal("300", identityProvider.MaxAcceptedAccessTokenLifetimeSeconds);
    }

    [Fact]
    public async Task ReadAsync_WithGenericApprovalCoreRunMigrations_ParsesBooleanTrue()
    {
        string yaml = """
            version: 1
            profiles:
              p:
                kind: mcp-stdio
                genericApprovalCore:
                  approvalRoot: /data/approvals
                  runMigrationsOnStartup: "true"
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        Assert.True(doc.Profiles[0].GenericApprovalCore?.RunMigrationsOnStartup);
    }

    [Fact]
    public async Task ReadAsync_MultipleProfiles_ReturnsAllProfiles()
    {
        string yaml = """
            version: 1
            profiles:
              profile-a:
                kind: mcp-stdio
                domainAdapters:
                  - name: a
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k
                      allowedNamespaces:
                        - default
              profile-b:
                kind: compose
                domainAdapters:
                  - name: b
                    type: kubernetes
                    kubernetes:
                      kubeconfig: /k2
                      allowedNamespaces:
                        - default
            """;
        string path = WriteYaml(yaml);

        var doc = await RunProfileDocumentReader.ReadAsync(path, CancellationToken.None);

        Assert.Equal(2, doc.Profiles.Count);
        Assert.Contains(doc.Profiles, p => p.Name == "profile-a");
        Assert.Contains(doc.Profiles, p => p.Name == "profile-b");
    }
}
