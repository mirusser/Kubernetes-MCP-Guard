using System.Text.Json.Nodes;
using InfraGate.Approvals;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.DownstreamAuth;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class DownstreamMcpClientTests
{
    [Fact]
    public void CreateTransportOptions_ExcludesGatewayClientSecret()
    {
        string secretKey = DownstreamAuthConventions.EnvironmentVariables.GatewayClientSecret;
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
        Environment.SetEnvironmentVariable(secretKey, "super-secret-value");
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.DoesNotContain(secretKey, transportOptions.EnvironmentVariables!.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretKey, null);
        }
    }

    [Fact]
    public void CreateTransportOptions_ExcludesGatewayClientId()
    {
        string clientIdKey = DownstreamAuthConventions.EnvironmentVariables.GatewayClientId;
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
        Environment.SetEnvironmentVariable(clientIdKey, "infra-gate-gateway");
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.DoesNotContain(clientIdKey, transportOptions.EnvironmentVariables!.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(clientIdKey, null);
        }
    }

    [Fact]
    public void CreateTransportOptions_ExcludesGatewayOAuthAuthority()
    {
        string key = GatewayAuthConventions.EnvironmentVariables.OAuthAuthority;
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
        Environment.SetEnvironmentVariable(key, "http://keycloak/realms/infra-gate");
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.DoesNotContain(key, transportOptions.EnvironmentVariables!.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Theory]
    [InlineData(RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, "Development")]
    [InlineData(RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment, "Production")]
    [InlineData(RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment, "Staging")]
    public void CreateTransportOptions_PassesThroughAllowedVar_WhenSet(string envVarName, string envVarValue)
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
        string? original = Environment.GetEnvironmentVariable(envVarName);
        Environment.SetEnvironmentVariable(envVarName, envVarValue);
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.Contains(envVarName, transportOptions.EnvironmentVariables!.Keys);
            Assert.Equal(envVarValue, transportOptions.EnvironmentVariables![envVarName]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, original);
        }
    }

    [Theory]
    [InlineData(ApprovalConventions.EnvironmentVariables.ApprovalRoot, "/mnt/approvals")]
    [InlineData(DownstreamAuthConventions.EnvironmentVariables.Required, "true")]
    [InlineData(DownstreamAuthConventions.EnvironmentVariables.Authority, "http://keycloak/realms/infra-gate")]
    [InlineData(DownstreamAuthConventions.EnvironmentVariables.Audience, "urn:infra-gate:mcp-server")]
    [InlineData(DownstreamAuthConventions.EnvironmentVariables.Scope, "mcp:downstream")]
    public void CreateTransportOptions_PassesThroughServerConfigVar_WhenSet(string envVarName, string envVarValue)
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
        string? original = Environment.GetEnvironmentVariable(envVarName);
        Environment.SetEnvironmentVariable(envVarName, envVarValue);
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.Contains(envVarName, transportOptions.EnvironmentVariables!.Keys);
            Assert.Equal(envVarValue, transportOptions.EnvironmentVariables![envVarName]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, original);
        }
    }

    [Fact]
    public void CreateTransportOptions_PassesThroughInfraGateConfigPath_WhenSet()
    {
        string configPath = "/app/config/appsettings.InfraGate.json";
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
        string? original = Environment.GetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath);
        Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, configPath);
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.Contains(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, transportOptions.EnvironmentVariables!.Keys);
            Assert.Equal(configPath, transportOptions.EnvironmentVariables![RuntimeSafetyConventions.EnvironmentVariables.ConfigPath]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(RuntimeSafetyConventions.EnvironmentVariables.ConfigPath, original);
        }
    }

    [Fact]
    public void CreateTransportOptions_UsesAssemblyArguments_WhenDownstreamAssemblySet()
    {
        string downstreamProject = "/app/server/InfraGate.McpServer.dll";

        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory(), downstreamAssembly: "/app/server/InfraGate.McpServer.dll");
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.NotNull(transportOptions.Arguments);
        string arguments = Assert.Single(transportOptions.Arguments!);
        Assert.Equal("/app/server/InfraGate.McpServer.dll", arguments);
    }

    [Fact]
    public void CreateTransportOptions_UsesRunProjectArguments_WhenDownstreamAssemblyNotSet()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.NotNull(transportOptions.Arguments);
        int argCount = transportOptions.Arguments!.Count;
        Assert.Equal(3, argCount);
        Assert.Equal(McpGatewayConventions.DownstreamProcess.RunArgument, transportOptions.Arguments![0]);
        Assert.Equal(McpGatewayConventions.DownstreamProcess.ProjectArgument, transportOptions.Arguments![1]);
        Assert.Equal(downstreamProject, transportOptions.Arguments![2]);
    }

    [Fact]
    public void CreateTransportOptions_UsesRunProjectArguments_WhenDownstreamAssemblyWhitespace()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory(), downstreamAssembly: "   ");
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.NotNull(transportOptions.Arguments);
        Assert.Equal(3, transportOptions.Arguments!.Count);
        Assert.Equal("run", transportOptions.Arguments![0]);
        Assert.Equal("--project", transportOptions.Arguments![1]);
    }

    [Fact]
    public void CreateTransportOptions_SetsWorkingDirectory()
    {
        string workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        try
        {
            string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
            var options = CreateOptions(downstreamProject, workingDirectory: workingDirectory);
            var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

            var transportOptions = client.CreateTransportOptions();

            Assert.Equal(workingDirectory, transportOptions.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateTransportOptions_SetsShutdownTimeout()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(TimeSpan.FromSeconds(10), transportOptions.ShutdownTimeout);
    }

    [Fact]
    public void CreateTransportOptions_SetsNameAndCommand()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(McpGatewayConventions.DownstreamProcess.Name, transportOptions.Name);
        Assert.Equal(McpGatewayConventions.DownstreamProcess.Command, transportOptions.Command);
    }

    [Fact]
    public void BuildAuthMeta_WithBearerToken_ReturnsMetaWithAuthKey()
    {
        string token = "Bearer eyJhbGciOiJSUzI1NiJ9.test.sig";

        var meta = DownstreamMcpClient.BuildAuthMeta(token);

        Assert.NotNull(meta);
        Assert.True(meta.ContainsKey(DownstreamAuthConventions.MetaKey));
        Assert.Equal(token, meta[DownstreamAuthConventions.MetaKey]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void BuildAuthMeta_WithEmptyOrNullToken_ReturnsNull(string? token)
    {
        var meta = DownstreamMcpClient.BuildAuthMeta(token!);

        Assert.Null(meta);
    }

    [Fact]
    public void BuildAuthMeta_TokenValue_DoesNotAppearInMetaKeyName()
    {
        string token = "Bearer supersecrettoken";

        var meta = DownstreamMcpClient.BuildAuthMeta(token);

        Assert.NotNull(meta);
        // The key must be the convention key, not the token value itself
        string key = Assert.Single(meta!.Select(kv => kv.Key));
        Assert.Equal(DownstreamAuthConventions.MetaKey, key);
        Assert.DoesNotContain("supersecrettoken", key);
    }

    [Fact]
    public void BuildBootstrapLine_WithBearerToken_ReturnsAuthorizationLine()
    {
        string token = "Bearer eyJhbGciOiJSUzI1NiJ9.test.sig";

        string? line = DownstreamMcpClient.BuildBootstrapLine(token);

        Assert.Equal($"{DownstreamAuthConventions.BootstrapLineKey}: {token}", line);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void BuildBootstrapLine_WithEmptyOrNullToken_ReturnsNull(string? token)
    {
        string? line = DownstreamMcpClient.BuildBootstrapLine(token!);

        Assert.Null(line);
    }

    [Fact]
    public void Constructor_AcceptsNullTokenProvider_ForDisabledAuthMode()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());

        // NullDownstreamServiceTokenProvider is the disabled-auth provider (Required=false)
        var client = new DownstreamMcpClient(options, new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        Assert.NotNull(client);
    }

    // --- IsDownstreamAuthRejection ---

    [Fact]
    public void IsDownstreamAuthRejection_McpExceptionWithAuthCode_ReturnsTrue()
    {
        var ex = new McpException($"{DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired}: token expired");

        bool result = DownstreamMcpClient.IsDownstreamAuthRejection(ex);

        Assert.True(result);
    }

    [Fact]
    public void IsDownstreamAuthRejection_McpExceptionWithUnrelatedMessage_ReturnsFalse()
    {
        var ex = new McpException("tool_not_found: no such tool");

        bool result = DownstreamMcpClient.IsDownstreamAuthRejection(ex);

        Assert.False(result);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(OperationCanceledException))]
    public void IsDownstreamAuthRejection_NonMcpException_ReturnsFalse(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "some message")!;

        bool result = DownstreamMcpClient.IsDownstreamAuthRejection(ex);

        Assert.False(result);
    }

    // --- WithAuthRetryAsync ---

    [Fact]
    public async Task WithAuthRetryAsync_SuccessOnFirstAttempt_ReturnsResultWithoutRefresh()
    {
        var tokenProvider = new FakeDownstreamServiceTokenProvider("first-token", "refreshed-token");
        var downstreamClient = CreateDownstreamMcpClient(tokenProvider);
        int callCount = 0;

        string result = await downstreamClient.WithAuthRetryAsync(token =>
        {
            callCount++;
            return Task.FromResult($"ok-{token}");
        }, CancellationToken.None);

        Assert.Equal("ok-first-token", result);
        Assert.Equal(1, callCount);
        Assert.Equal(0, tokenProvider.RefreshCallCount);
    }

    [Fact]
    public async Task WithAuthRetryAsync_AuthRejectionOnFirstAttempt_RefreshesAndRetries()
    {
        var tokenProvider = new FakeDownstreamServiceTokenProvider("first-token", "refreshed-token");
        var downstreamClient = CreateDownstreamMcpClient(tokenProvider);
        int callCount = 0;

        string result = await downstreamClient.WithAuthRetryAsync(token =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new McpException($"{DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired}: expired");
            }
            return Task.FromResult($"ok-{token}");
        }, CancellationToken.None);

        Assert.Equal("ok-refreshed-token", result);
        Assert.Equal(2, callCount);
        Assert.Equal(1, tokenProvider.RefreshCallCount);
    }

    [Fact]
    public async Task WithAuthRetryAsync_NonAuthExceptionOnFirstAttempt_DoesNotRetry()
    {
        var tokenProvider = new FakeDownstreamServiceTokenProvider("first-token", "refreshed-token");
        var downstreamClient = CreateDownstreamMcpClient(tokenProvider);

        await Assert.ThrowsAsync<McpException>(() => downstreamClient.WithAuthRetryAsync<string>(token =>
        {
            throw new McpException("tool_not_found: missing");
        }, CancellationToken.None));

        Assert.Equal(0, tokenProvider.RefreshCallCount);
    }

    [Fact]
    public async Task WithAuthRetryAsync_AuthRejectionAfterRefresh_ThrowsMcpExceptionWithoutTokenContent()
    {
        string sensitiveToken = "Bearer super-secret-refreshed-token";
        var tokenProvider = new FakeDownstreamServiceTokenProvider("first-token", sensitiveToken);
        var downstreamClient = CreateDownstreamMcpClient(tokenProvider);

        var ex = await Assert.ThrowsAsync<McpException>(() => downstreamClient.WithAuthRetryAsync<string>(token =>
        {
            throw new McpException($"{DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired}: rejected");
        }, CancellationToken.None));

        // The final exception message must NOT contain the token value
        Assert.DoesNotContain(sensitiveToken, ex.Message);
        // The message must reference the config key so operators know where to look
        Assert.Contains(DownstreamAuthConventions.EnvironmentVariables.GatewayClientId, ex.Message);
    }

    [Fact]
    public async Task WithAuthRetryAsync_AuthRejectionAfterRefresh_WrapsOriginalException()
    {
        var tokenProvider = new FakeDownstreamServiceTokenProvider("first-token", "refreshed-token");
        var downstreamClient = CreateDownstreamMcpClient(tokenProvider);

        var ex = await Assert.ThrowsAsync<McpException>(() => downstreamClient.WithAuthRetryAsync<string>(token =>
        {
            throw new McpException($"{DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired}: rejected");
        }, CancellationToken.None));

        Assert.NotNull(ex.InnerException);
        Assert.IsType<McpException>(ex.InnerException);
    }

    private static DownstreamMcpClient CreateDownstreamMcpClient(IDownstreamServiceTokenProvider tokenProvider)
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        return new DownstreamMcpClient(options, tokenProvider, NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
    }

    private static McpGatewayOptions CreateOptions(
        string downstreamProject,
        string workingDirectory,
        string? downstreamAssembly = null)
    {
        var authOptions = new GatewayAuthOptions(
            OAuthAuthority: "http://127.0.0.1:3010/realms/infra-gate",
            OAuthResource: GatewayAuthConventions.DefaultOAuthResource,
            OAuthScope: GatewayAuthConventions.DefaultOAuthScope,
            OAuthRequireHttpsMetadata: false);

        return new McpGatewayOptions(
            authOptions,
            DownstreamProject: downstreamProject,
            GuardAuditRoot: Path.Combine(Path.GetTempPath(), "audit"),
            WorkingDirectory: workingDirectory,
            ApprovalRoot: Path.Combine(Path.GetTempPath(), "approvals"),
            ApprovalBaseUrl: null,
            ApprovalChallengeTtl: McpGatewayOptions.DefaultApprovalChallengeTtl,
            DownstreamAssembly: downstreamAssembly);
    }
}

internal sealed class FakeDownstreamServiceTokenProvider : IDownstreamServiceTokenProvider
{
    private readonly string initialToken;
    private readonly string refreshedToken;

    public int RefreshCallCount { get; private set; }

    internal FakeDownstreamServiceTokenProvider(string initialToken, string refreshedToken)
    {
        this.initialToken = initialToken;
        this.refreshedToken = refreshedToken;
    }

    public Task<string> GetServiceTokenAsync(CancellationToken cancellationToken) =>
        Task.FromResult(initialToken);

    public Task<string> RefreshServiceTokenAsync(CancellationToken cancellationToken)
    {
        RefreshCallCount++;
        return Task.FromResult(refreshedToken);
    }
}
