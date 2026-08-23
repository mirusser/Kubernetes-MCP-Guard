using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.Execution;
using InfraGate.DownstreamAuth;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.DownstreamAuth;
using InfraGate.McpGateway.Tests.Fakes;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class DownstreamMcpClientTests
{
    [Fact]
    public async Task CallToolAsync_Success_RecordsSuccessMetricAndPropagatesTraceContext()
    {
        var recordedCalls = new List<Measurement<long>>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == McpGatewayConventions.Telemetry.DownstreamCallCounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recordedCalls.Add(new Measurement<long>(value, tags)));
        meterListener.Start();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == McpGatewayConventions.Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(activityListener);

        string controlDir = ControlFile.CreateDirectory();
        await using DownstreamMcpClient client = CreateFixtureClient(controlDir);

        DownstreamCallResult result = await client.CallToolAsync(
            "echo-meta",
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.False(result.IsError);
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        JsonObject echoedMeta = JsonNode.Parse(textBlock.Text)!.AsObject();
        string traceparent = echoedMeta[McpGatewayConventions.Telemetry.MetaKeys.TraceParent]!.GetValue<string>();
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", traceparent);

        Measurement<long> measurement = Assert.Single(recordedCalls);
        Assert.Equal(McpGatewayConventions.Telemetry.Outcomes.Success, TagValue(measurement, McpGatewayConventions.Telemetry.Tags.Outcome));
        Assert.Equal("echo-meta", TagValue(measurement, McpGatewayConventions.Telemetry.Tags.ToolName));
        Assert.Equal(McpGatewayConventions.DownstreamSources.Primary, TagValue(measurement, McpGatewayConventions.Telemetry.Tags.Source));
    }

    [Fact]
    public async Task CallToolAsync_ToolReturnsError_RecordsMcpErrorMetric()
    {
        var recordedCalls = new List<Measurement<long>>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == McpGatewayConventions.Telemetry.DownstreamCallCounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recordedCalls.Add(new Measurement<long>(value, tags)));
        meterListener.Start();

        string controlDir = ControlFile.CreateDirectory();
        await using DownstreamMcpClient client = CreateFixtureClient(controlDir);

        DownstreamCallResult result = await client.CallToolAsync(
            "fail",
            new Dictionary<string, object?>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.False(result.IsTransportFault);

        Measurement<long> measurement = Assert.Single(recordedCalls);
        Assert.Equal(McpGatewayConventions.Telemetry.Outcomes.McpError, TagValue(measurement, McpGatewayConventions.Telemetry.Tags.Outcome));
        Assert.Equal("fail", TagValue(measurement, McpGatewayConventions.Telemetry.Tags.ToolName));
    }

    [Fact]
    public async Task CallToolAsync_ProcessCrashes_RecordsTransportErrorMetric()
    {
        var recordedCalls = new List<Measurement<long>>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == McpGatewayConventions.Telemetry.DownstreamCallCounterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            recordedCalls.Add(new Measurement<long>(value, tags)));
        meterListener.Start();

        string controlDir = ControlFile.CreateDirectory();
        await using DownstreamMcpClient client = CreateFixtureClient(controlDir);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        DownstreamCallResult warmup = await client.CallToolAsync("ping", new Dictionary<string, object?>(StringComparer.Ordinal), cts.Token);
        Assert.False(warmup.IsError);

        ControlFile.WriteCommand(controlDir, "crash");
        await Task.Delay(TimeSpan.FromMilliseconds(150), TimeProvider.System, cts.Token);

        DownstreamCallResult result = await client.CallToolAsync("ping", new Dictionary<string, object?>(StringComparer.Ordinal), cts.Token);

        Assert.True(result.IsError);
        Assert.True(result.IsTransportFault);

        Measurement<long> transportErrorMeasurement = Assert.Single(
            recordedCalls,
            m => (string?)TagValue(m, McpGatewayConventions.Telemetry.Tags.Outcome) == McpGatewayConventions.Telemetry.Outcomes.TransportError);
        Assert.Equal("ping", TagValue(transportErrorMeasurement, McpGatewayConventions.Telemetry.Tags.ToolName));
    }

    [Fact]
    public async Task DisposeAsync_AfterEstablishedConnection_TerminatesChildProcessWithNoOrphan()
    {
        string controlDir = ControlFile.CreateDirectory();
        DownstreamMcpClient client = CreateFixtureClient(controlDir);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        DownstreamCallResult warmup = await client.CallToolAsync("ping", new Dictionary<string, object?>(StringComparer.Ordinal), cts.Token);
        Assert.False(warmup.IsError);

        int pid = ControlFile.ReadPid(controlDir);
        using Process childProcess = Process.GetProcessById(pid);
        Assert.False(childProcess.HasExited);

        await client.DisposeAsync();

        var stopwatch = Stopwatch.StartNew();
        while (!childProcess.HasExited && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), TimeProvider.System, cts.Token);
        }

        Assert.True(childProcess.HasExited, "Child process was not terminated after DisposeAsync.");
    }

    private static DownstreamMcpClient CreateFixtureClient(string controlDir)
    {
        string fixtureDllPath = ProcessFixtureLocator.ResolveDllPath();
        var descriptor = new DownstreamProcessDescriptor(
            "process-fixture",
            "dotnet",
            [fixtureDllPath, "--control-dir", controlDir],
            Directory.GetCurrentDirectory(),
            AuthRequired: false,
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string?>(StringComparer.Ordinal));

        return new DownstreamMcpClient(
            descriptor,
            new NullDownstreamServiceTokenProvider(),
            NullLogger<DownstreamMcpClient>.Instance,
            NullLoggerFactory.Instance);
    }

    private static object? TagValue(Measurement<long> measurement, string key) =>
        measurement.Tags.ToArray().First(t => t.Key == key).Value;

    [Fact]
    public void CreateTransportOptions_ExcludesGatewayClientSecret()
    {
        string secretKey = DownstreamAuthConventions.EnvironmentVariables.GatewayClientSecret;
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
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
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
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
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
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
    [InlineData("K8S_MCP_APPROVAL_ROOT", "/mnt/approvals")]
    [InlineData("K8S_MCP_ALLOWED_NAMESPACES", "mcp-nginx-demo")]
    [InlineData("K8S_MCP_USE_IN_CLUSTER", "true")]
    [InlineData("K8S_MCP_LOG_PATH", "/data/logs/mcp-server.log")]
    [InlineData("KUBECONFIG", "/home/user/.kube/config")]
    public void CreateTransportOptions_ExcludesOldStyleEnvVar_AfterHardCut(string envVarName, string envVarValue)
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
        string? original = Environment.GetEnvironmentVariable(envVarName);
        Environment.SetEnvironmentVariable(envVarName, envVarValue);
        try
        {
            var transportOptions = client.CreateTransportOptions();

            Assert.DoesNotContain(envVarName, transportOptions.EnvironmentVariables!.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, original);
        }
    }

    [Theory]
    [InlineData("InfraGate__Runtime__Environment", "Development")]
    [InlineData(RuntimeSafetyConventions.EnvironmentVariables.DotNetEnvironment, "Production")]
    [InlineData(RuntimeSafetyConventions.EnvironmentVariables.AspNetCoreEnvironment, "Staging")]
    public void CreateTransportOptions_PassesThroughAllowedVar_WhenSet(string envVarName, string envVarValue)
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
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
    [InlineData("InfraGate__DownstreamAuth__Required", "true")]
    [InlineData("InfraGate__DownstreamAuth__Authority", "http://keycloak/realms/infra-gate")]
    [InlineData("InfraGate__DownstreamAuth__Audience", "urn:infra-gate:mcp-server")]
    [InlineData("InfraGate__DownstreamAuth__Scope", "mcp:downstream")]
    [InlineData("InfraGate__Kubernetes__AllowedNamespaces__0", "mcp-nginx-demo")]
    [InlineData("InfraGate__Kubernetes__UseInClusterConfig", "true")]
    [InlineData("InfraGate__Kubernetes__LogPath", "/data/logs/mcp-server.log")]
    public void CreateTransportOptions_PassesThroughServerConfigVar_WhenSet(string envVarName, string envVarValue)
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
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
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
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
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

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
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

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
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

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
            var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

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
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

        var transportOptions = client.CreateTransportOptions();

        Assert.Equal(TimeSpan.FromSeconds(10), transportOptions.ShutdownTimeout);
    }

    [Fact]
    public void CreateTransportOptions_SetsNameAndCommand()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

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
    public void CreateClientOptions_WithBearerToken_SetsInitializeMeta()
    {
        string token = "Bearer eyJhbGciOiJSUzI1NiJ9.test.sig";

        var options = DownstreamMcpClient.CreateClientOptions(token);

        Assert.Equal(token, options.InitializeMeta?[DownstreamAuthConventions.MetaKey]?.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void CreateClientOptions_WithEmptyOrNullToken_OmitsInitializeMeta(string? token)
    {
        var options = DownstreamMcpClient.CreateClientOptions(token!);

        Assert.Null(options.InitializeMeta);
    }

    [Fact]
    public void Constructor_AcceptsNullTokenProvider_ForDisabledAuthMode()
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());

        // NullDownstreamServiceTokenProvider is the disabled-auth provider (Required=false)
        var client = new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), new NullDownstreamServiceTokenProvider(), NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);

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

    [Fact]
    public void DownstreamCallResult_FromCallToolResult_PreservesTextContent()
    {
        var callToolResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "pod-name: nginx-123" }],
            IsError = false
        };

        DownstreamCallResult result = DownstreamCallResult.FromCallToolResult(callToolResult);

        Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Equal("pod-name: nginx-123", textBlock.Text);
        Assert.False(result.IsError);
    }

    [Fact]
    public void DownstreamCallResult_FromCallToolResult_PreservesMultipleContentBlocks()
    {
        var callToolResult = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "First block" },
                new TextContentBlock { Text = "Second block" }
            ],
            IsError = false
        };

        DownstreamCallResult result = DownstreamCallResult.FromCallToolResult(callToolResult);

        Assert.Equal(2, result.Content.Count);
        Assert.Equal("First block", ((TextContentBlock)result.Content[0]).Text);
        Assert.Equal("Second block", ((TextContentBlock)result.Content[1]).Text);
    }

    [Fact]
    public void DownstreamCallResult_FromCallToolResult_PreservesIsError()
    {
        var callToolResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "error detail" }],
            IsError = true
        };

        DownstreamCallResult result = DownstreamCallResult.FromCallToolResult(callToolResult);

        Assert.True(result.IsError);
    }

    [Fact]
    public void DownstreamCallResult_FromCallToolResult_PreservesMeta()
    {
        var meta = new JsonObject { ["traceId"] = "abc123" };
        var callToolResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "result" }],
            IsError = false,
            Meta = meta
        };

        DownstreamCallResult result = DownstreamCallResult.FromCallToolResult(callToolResult);

        Assert.NotNull(result.Meta);
        Assert.Equal("abc123", result.Meta["traceId"]!.GetValue<string>());
    }

    [Fact]
    public void DownstreamCallResult_FromTransportException_CreatesErrorResult()
    {
        var exception = new InvalidOperationException("Connection lost");

        DownstreamCallResult result = DownstreamCallResult.FromTransportException(exception);

        Assert.True(result.IsError);
        Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Contains("InvalidOperationException", textBlock.Text);
        Assert.Contains("Connection lost", textBlock.Text);
    }

    [Fact]
    public void DownstreamCallResult_FromTransportException_DoesNotLeakStackTrace()
    {
        Exception exception;
        try
        {
            throw new InvalidOperationException("Simulated transport error");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        DownstreamCallResult result = DownstreamCallResult.FromTransportException(exception);

        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.DoesNotContain("at InfraGate", textBlock.Text);
        Assert.DoesNotContain("StackTrace", textBlock.Text);
    }

    private static DownstreamMcpClient CreateDownstreamMcpClient(IDownstreamServiceTokenProvider tokenProvider)
    {
        string downstreamProject = "/app/src/InfraGate.McpServer/InfraGate.McpServer.csproj";
        var options = CreateOptions(downstreamProject, workingDirectory: Directory.GetCurrentDirectory());
        return new DownstreamMcpClient(DownstreamProcessDescriptor.ForPrimary(options), tokenProvider, NullLogger<DownstreamMcpClient>.Instance, NullLoggerFactory.Instance);
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
