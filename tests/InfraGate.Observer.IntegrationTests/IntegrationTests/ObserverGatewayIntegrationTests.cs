using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using InfraGate.AgentLlm;
using InfraGate.ClientCredentials;
using InfraGate.Observer.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// ASPDEPR004/ASPDEPR008: WebHostBuilder + TestServer are deprecated in favor of WebApplicationBuilder.
// Suppressed because: these integration tests need an in-process MCP gateway endpoint without binding a real port.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.Observer.IntegrationTests.IntegrationTests;

public sealed class ObserverGatewayIntegrationTests
{
    private const string NamespaceName = "mcp-nginx-demo";

    [Fact]
    public async Task RunAsync_FailingDeploymentFixture_ProducesExpectedPerResourceReports()
    {
        await using var gateway = ObserverGatewayFixture.Create();
        await using var mcpClient = await gateway.CreateObserverClientAsync();
        var sink = new CapturingAnomalyHandoffSink();
        var runner = CreateRunner(mcpClient, sink, CreateSnapshotDrivenChatClient());

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.False(result.IsTruncated);
        Assert.Equal(3, result.Reports.Count);
        Assert.Contains(result.Reports, report =>
            report.Kind == AnomalyKind.DeploymentUnavailable &&
            report.Target.Kind == "Deployment" &&
            report.Target.Name == "nginx-demo" &&
            report.Severity == Severity.High);
        Assert.Contains(result.Reports, report =>
            report.Kind == AnomalyKind.ServiceNoEndpoints &&
            report.Target.Kind == "Service" &&
            report.Target.Name == "nginx-demo" &&
            report.Severity == Severity.High);
        Assert.Contains(result.Reports, report =>
            report.Kind == AnomalyKind.PodUnhealthy &&
            report.Target.Kind == "Pod" &&
            report.Target.Name == "nginx-demo-5fdb9f6b7c-x7z5q" &&
            report.Severity == Severity.Medium);

        Assert.Contains(gateway.Calls, call => call.ToolName == ObserverConventions.ToolNames.GetK8sStatus);
        Assert.Contains(gateway.Calls, call => call.ToolName == ObserverConventions.ToolNames.GetK8sEvents);
        Assert.Contains(gateway.Calls, call => call.ToolName == ObserverConventions.ToolNames.GetK8sPods);
        Assert.Contains(gateway.Calls, call => call.ToolName == ObserverConventions.ToolNames.GetK8sDeployments);
        Assert.Contains(gateway.Calls, call => call.ToolName == ObserverConventions.ToolNames.GetK8sServices);
        Assert.Contains(gateway.Calls, call => call.ToolName == ObserverConventions.ToolNames.GetK8sEndpoints);
        Assert.All(gateway.Calls, call => Assert.Contains(call.ToolName, ObserverConventions.ToolNames.ReadOnlyToolNames));
        Assert.All(gateway.AuthorizationHeaders, header => Assert.Equal("Bearer observer-token", header));
        Assert.True(gateway.TokenProvider.GetTokenCalls > 0);

        var batch = Assert.Single(sink.Batches);
        Assert.Equal(result.CycleId, batch.CycleId);
        Assert.Equal(result.Reports.Count, batch.Reports.Count);
    }

    [Fact]
    public async Task RunAsync_RepeatedFailingDeployment_KeepsAnomalyIdsStable()
    {
        await using var gateway = ObserverGatewayFixture.Create();
        await using var mcpClient = await gateway.CreateObserverClientAsync();
        var dedupeStore = new AnomalyDedupeStore();
        var options = DefaultOptions() with { DedupeSuppressionWindow = 1 };

        var first = await CreateRunner(
            mcpClient,
            new CapturingAnomalyHandoffSink(),
            CreateSnapshotDrivenChatClient(),
            dedupeStore,
            options).RunAsync(CancellationToken.None);
        var second = await CreateRunner(
            mcpClient,
            new CapturingAnomalyHandoffSink(),
            CreateSnapshotDrivenChatClient(),
            dedupeStore,
            options).RunAsync(CancellationToken.None);

        Assert.Equal(3, first.Reports.Count);
        Assert.Equal(3, second.Reports.Count);
        Assert.Equal(
            first.Reports.Select(report => report.AnomalyId).Order(StringComparer.Ordinal),
            second.Reports.Select(report => report.AnomalyId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RunAsync_FixedDeployment_EmitsResolvedReportsWithinTwoCycles()
    {
        await using var gateway = ObserverGatewayFixture.Create();
        await using var mcpClient = await gateway.CreateObserverClientAsync();
        var dedupeStore = new AnomalyDedupeStore();
        var options = DefaultOptions() with { DedupeResolutionThreshold = 2 };

        var active = await CreateRunner(
            mcpClient,
            new CapturingAnomalyHandoffSink(),
            CreateSnapshotDrivenChatClient(),
            dedupeStore,
            options).RunAsync(CancellationToken.None);

        gateway.UseFixedDeploymentSnapshot();

        var firstFixedCycle = await CreateRunner(
            mcpClient,
            new CapturingAnomalyHandoffSink(),
            CreateSnapshotDrivenChatClient(),
            dedupeStore,
            options).RunAsync(CancellationToken.None);
        var secondFixedCycle = await CreateRunner(
            mcpClient,
            new CapturingAnomalyHandoffSink(),
            CreateSnapshotDrivenChatClient(),
            dedupeStore,
            options).RunAsync(CancellationToken.None);

        Assert.Equal(3, active.Reports.Count);
        Assert.Empty(firstFixedCycle.Reports);
        Assert.Equal(3, secondFixedCycle.Reports.Count);
        Assert.All(secondFixedCycle.Reports, report =>
        {
            Assert.Equal(AnomalyStatus.Resolved, report.Status);
            Assert.Equal(Severity.Low, report.Severity);
        });
        Assert.Equal(
            active.Reports.Select(report => report.AnomalyId).Order(StringComparer.Ordinal),
            secondFixedCycle.Reports.Select(report => report.AnomalyId).Order(StringComparer.Ordinal));
    }

    private static ObservationCycleRunner CreateRunner(
        IObserverMcpClient mcpClient,
        IAnomalyHandoffSink sink,
        FixtureChatClient chatClientFactory,
        IAnomalyDedupeStore? dedupeStore = null,
        ObserverOptions? options = null)
    {
        var observerOptions = options ?? DefaultOptions();
        var optionsMonitor = new FixedOptionsMonitor<ObserverOptions>(observerOptions);

        return new ObservationCycleRunner(
            optionsMonitor,
            new SnapshotFetcher(mcpClient, NullLogger<SnapshotFetcher>.Instance, ObserverMetrics.Meter),
            new SystemPromptProvider(),
            new ToolCallingAgentFactory(chatClientFactory),
            new SeverityClassifier(),
            mcpClient,
            dedupeStore ?? new AnomalyDedupeStore(),
            sink,
            NullLogger<ObservationCycleRunner>.Instance,
            ObserverMetrics.Meter);
    }

    private static ObserverOptions DefaultOptions()
    {
        return new ObserverOptions
        {
            GatewayBaseUrl = "http://localhost/mcp",
            AllowedNamespaces = [NamespaceName],
            CycleIntervalSeconds = 60,
            WallClockCapSeconds = 20,
            MaxToolIterations = 8,
            DedupeSuppressionWindow = 5,
            DedupeResolutionThreshold = 2,
        };
    }

    private static FixtureChatClient CreateSnapshotDrivenChatClient()
    {
        return new FixtureChatClient(messages =>
        {
            string snapshot = messages.Last(message => message.Role == ChatRole.User).Text ?? string.Empty;
            string response = snapshot.Contains("nginx:1.27-doesnotexist", StringComparison.Ordinal)
                ? FailingDeploymentReportsJson()
                : "[]";

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, response));
        });
    }

    private static string FailingDeploymentReportsJson()
    {
        return """
        [
          {
            "Kind": "DeploymentUnavailable",
            "Severity": "High",
            "Target": {
              "ApiVersion": "apps/v1",
              "Kind": "Deployment",
              "Namespace": "mcp-nginx-demo",
              "Name": "nginx-demo"
            },
            "Summary": "Deployment has no available replicas.",
            "Evidence": [],
            "Annotations": {
              "ReplicasDesired": "2",
              "ReplicasAvailable": "0"
            }
          },
          {
            "Kind": "ServiceNoEndpoints",
            "Severity": "High",
            "Target": {
              "ApiVersion": "v1",
              "Kind": "Service",
              "Namespace": "mcp-nginx-demo",
              "Name": "nginx-demo"
            },
            "Summary": "Service has no ready endpoints.",
            "Evidence": [],
            "Annotations": {
              "EndpointCount": "0"
            }
          },
          {
            "Kind": "PodUnhealthy",
            "Severity": "Medium",
            "Target": {
              "ApiVersion": "v1",
              "Kind": "Pod",
              "Namespace": "mcp-nginx-demo",
              "Name": "nginx-demo-5fdb9f6b7c-x7z5q"
            },
            "Summary": "Pod cannot pull the configured image.",
            "Evidence": [],
            "Annotations": {
              "PodCondition": "ImagePullBackOff",
              "HasHealthySiblings": "true"
            }
          }
        ]
        """;
    }

    private sealed class ObserverGatewayFixture : IAsyncDisposable
    {
        public const string McpPath = "/mcp";
        private readonly TestServer server;
        private bool useFixedDeploymentSnapshot;

        private ObserverGatewayFixture(TestServer server, StubTokenProvider tokenProvider)
        {
            this.server = server;
            TokenProvider = tokenProvider;
        }

        public StubTokenProvider TokenProvider { get; }

        public ConcurrentBag<GatewayToolCall> Calls { get; } = [];

        public ConcurrentBag<string> AuthorizationHeaders { get; } = [];

        public static ObserverGatewayFixture Create()
        {
            var tokenProvider = new StubTokenProvider();
            ObserverGatewayFixture? fixture = null;

            var server = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services
                        .AddMcpServer()
                        .WithHttpTransport(transportOptions =>
                        {
                            transportOptions.Stateless = false;
                        })
                        .WithListToolsHandler((_, _) =>
                            new ValueTask<ListToolsResult>(new ListToolsResult { Tools = CreateTools() }))
                        .WithCallToolHandler((request, _) =>
                        {
                            if (fixture is null)
                            {
                                throw new InvalidOperationException("Gateway fixture was not initialised.");
                            }

                            string toolName = request.Params?.Name
                                ?? throw new InvalidOperationException("MCP call did not include a tool name.");
                            IReadOnlyDictionary<string, object?> arguments =
                                request.Params.Arguments is null
                                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                                    : request.Params.Arguments.ToDictionary(
                                        pair => pair.Key,
                                        pair => (object?)pair.Value,
                                        StringComparer.Ordinal);
                            fixture.Calls.Add(new GatewayToolCall(toolName, arguments));

                            string text = fixture.GetToolResponse(toolName);
                            return new ValueTask<CallToolResult>(new CallToolResult
                            {
                                Content = [new TextContentBlock { Text = text }]
                            });
                        });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.Use(async (context, next) =>
                    {
                        if (context.Request.Path.StartsWithSegments(McpPath))
                        {
                            if (fixture is null)
                            {
                                throw new InvalidOperationException("Gateway fixture was not initialised.");
                            }

                            string authorization = context.Request.Headers.Authorization.ToString();
                            fixture.AuthorizationHeaders.Add(authorization);

                            if (!string.Equals(authorization, "Bearer observer-token", StringComparison.Ordinal))
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                                return;
                            }
                        }

                        await next(context).ConfigureAwait(false);
                    });
                    app.UseEndpoints(endpoints => endpoints.MapMcp(McpPath));
                }));

            fixture = new ObserverGatewayFixture(server, tokenProvider);
            return fixture;
        }

        public async Task<HttpMcpObserverClient> CreateObserverClientAsync()
        {
            var client = new HttpMcpObserverClient(server, TokenProvider);
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            return client;
        }

        public void UseFixedDeploymentSnapshot()
        {
            useFixedDeploymentSnapshot = true;
        }

        public ValueTask DisposeAsync()
        {
            server.Dispose();
            return ValueTask.CompletedTask;
        }

        private string GetToolResponse(string toolName)
        {
            bool isFixed = useFixedDeploymentSnapshot;

            return toolName switch
            {
                ObserverConventions.ToolNames.GetAllowedNamespaces =>
                    """{ "namespaces": [ "mcp-nginx-demo" ] }""",
                ObserverConventions.ToolNames.GetK8sStatus => isFixed ? FixedStatusJson : FailingStatusJson,
                ObserverConventions.ToolNames.GetK8sEvents => isFixed ? FixedEventsJson : FailingEventsJson,
                ObserverConventions.ToolNames.GetK8sPods => isFixed ? FixedPodsJson : FailingPodsJson,
                ObserverConventions.ToolNames.GetK8sDeployments => isFixed ? FixedDeploymentsJson : FailingDeploymentsJson,
                ObserverConventions.ToolNames.GetK8sServices => ServicesJson,
                ObserverConventions.ToolNames.GetK8sEndpoints => isFixed ? FixedEndpointsJson : FailingEndpointsJson,
                ObserverConventions.ToolNames.DescribeK8sResource => "{}",
                _ => throw new InvalidOperationException($"Unexpected tool '{toolName}'."),
            };
        }

        private static IList<Tool> CreateTools()
        {
            var schema = JsonSerializer.SerializeToElement(new { type = "object" });
            return ObserverConventions.ToolNames.ReadOnlyToolNames
                .Select(toolName => new Tool
                {
                    Name = toolName,
                    Description = $"Stubbed read-only tool {toolName}.",
                    InputSchema = schema,
                })
                .ToList();
        }

        private const string FailingStatusJson = """
        {
          "namespace": "mcp-nginx-demo",
          "deployment": "nginx-demo",
          "image": "nginx:1.27-doesnotexist",
          "status": "Degraded"
        }
        """;

        private const string FixedStatusJson = """
        {
          "namespace": "mcp-nginx-demo",
          "deployment": "nginx-demo",
          "image": "nginx:1.27-alpine",
          "status": "Healthy"
        }
        """;

        private const string FailingEventsJson = """
        {
          "events": [
            {
              "type": "Warning",
              "reason": "Failed",
              "message": "Failed to pull image nginx:1.27-doesnotexist"
            }
          ]
        }
        """;

        private const string FixedEventsJson = """{ "events": [] }""";

        private const string FailingPodsJson = """
        {
          "pods": [
            {
              "metadata": { "name": "nginx-demo-5fdb9f6b7c-x7z5q" },
              "status": {
                "phase": "Pending",
                "containerStatuses": [
                  {
                    "state": { "waiting": { "reason": "ImagePullBackOff" } },
                    "image": "nginx:1.27-doesnotexist"
                  }
                ]
              }
            }
          ]
        }
        """;

        private const string FixedPodsJson = """
        {
          "pods": [
            {
              "metadata": { "name": "nginx-demo-5fdb9f6b7c-x7z5q" },
              "status": {
                "phase": "Running",
                "containerStatuses": [
                  {
                    "ready": true,
                    "image": "nginx:1.27-alpine"
                  }
                ]
              }
            }
          ]
        }
        """;

        private const string FailingDeploymentsJson = """
        {
          "deployments": [
            {
              "metadata": { "name": "nginx-demo" },
              "spec": { "replicas": 2 },
              "status": { "availableReplicas": 0 }
            }
          ]
        }
        """;

        private const string FixedDeploymentsJson = """
        {
          "deployments": [
            {
              "metadata": { "name": "nginx-demo" },
              "spec": { "replicas": 2 },
              "status": { "availableReplicas": 2 }
            }
          ]
        }
        """;

        private const string ServicesJson = """
        {
          "services": [
            {
              "metadata": { "name": "nginx-demo" },
              "spec": { "selector": { "app.kubernetes.io/name": "nginx-demo" } }
            }
          ]
        }
        """;

        private const string FailingEndpointsJson = """
        {
          "endpoints": [
            {
              "service": "nginx-demo",
              "addresses": []
            }
          ]
        }
        """;

        private const string FixedEndpointsJson = """
        {
          "endpoints": [
            {
              "service": "nginx-demo",
              "addresses": [ "10.244.0.42" ]
            }
          ]
        }
        """;
    }

    private sealed class HttpMcpObserverClient : IObserverMcpClient, IAsyncDisposable
    {
        private readonly TestServer server;
        private readonly IClientCredentialsTokenProvider tokenProvider;
        private HttpClient? httpClient;
        private McpClient? mcpClient;

        public HttpMcpObserverClient(TestServer server, IClientCredentialsTokenProvider tokenProvider)
        {
            this.server = server;
            this.tokenProvider = tokenProvider;
            GatewayBaseUrl = new Uri(server.BaseAddress, ObserverGatewayFixture.McpPath).ToString();
        }

        public string GatewayBaseUrl { get; }

        public bool IsConnected => mcpClient is not null;

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (mcpClient is not null)
            {
                return;
            }

            var bearerHandler = new ClientCredentialsBearerHandler(
                tokenProvider,
                NullLogger<ClientCredentialsBearerHandler>.Instance)
            {
                InnerHandler = server.CreateHandler()
            };

            httpClient = new HttpClient(bearerHandler)
            {
                BaseAddress = server.BaseAddress
            };

            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(httpClient.BaseAddress, ObserverGatewayFixture.McpPath),
                    Name = "infra-gate-observer-integration-test",
                    TransportMode = HttpTransportMode.StreamableHttp,
                },
                httpClient,
                NullLoggerFactory.Instance,
                ownsHttpClient: false);

            mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<AITool>> GetReadOnlyToolsAsync(CancellationToken cancellationToken)
        {
            if (mcpClient is null)
                throw new InvalidOperationException("MCP client is not connected. Call ConnectAsync first.");
            var allTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return allTools
                .Where(t => ObserverConventions.ToolNames.ReadOnlyToolNames.Contains(t.Name))
                .Cast<AITool>()
                .ToList();
        }

        public async Task<string?> GetToolResultAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            ToolWhitelist.AssertAllowed(toolName);

            if (mcpClient is null)
            {
                throw new InvalidOperationException("MCP client is not connected. Call ConnectAsync first.");
            }

            var result = await mcpClient.CallToolAsync(
                toolName,
                arguments,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return JsonSerializer.Serialize(result);
        }

        public async ValueTask DisposeAsync()
        {
            if (mcpClient is not null)
            {
                await mcpClient.DisposeAsync().ConfigureAwait(false);
            }

            httpClient?.Dispose();
        }
    }

    private sealed class StubTokenProvider : IClientCredentialsTokenProvider
    {
        private int getTokenCalls;
        private int refreshTokenCalls;

        public int GetTokenCalls => getTokenCalls;

        public int RefreshTokenCalls => refreshTokenCalls;

        public Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref getTokenCalls);
            return Task.FromResult("observer-token");
        }

        public Task<string> RefreshTokenAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref refreshTokenCalls);
            return Task.FromResult("observer-token");
        }
    }

    private sealed class CapturingAnomalyHandoffSink : IAnomalyHandoffSink
    {
        public List<AnomalyHandoffBatch> Batches { get; } = [];

        public Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T currentValue;

        public FixedOptionsMonitor(T currentValue)
        {
            this.currentValue = currentValue;
        }

        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FixtureChatClient : IChatClient, IChatClientFactory
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatResponse> responseFactory;

        public FixtureChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        public IChatClient Create() => this;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(responseFactory(messages));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }

        object? IChatClient.GetService(Type serviceType, object? serviceKey)
        {
            return null;
        }
    }

    private sealed record class GatewayToolCall(
        string ToolName,
        IReadOnlyDictionary<string, object?> Arguments);
}
