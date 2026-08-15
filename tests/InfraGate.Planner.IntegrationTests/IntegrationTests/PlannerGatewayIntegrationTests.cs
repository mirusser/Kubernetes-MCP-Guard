// ASPDEPR004/ASPDEPR008: WebHostBuilder + TestServer are deprecated in favor of WebApplicationBuilder.
// Suppressed because: these integration tests need an in-process MCP gateway endpoint without binding a real port.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using A2A;
using InfraGate.AgentLlm;
using InfraGate.AgentMcp;
using InfraGate.ClientCredentials;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Handoff;
using InfraGate.Planner.Tasks;
using InfraGate.Prompts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.Planner.IntegrationTests.IntegrationTests;

public sealed class PlannerGatewayIntegrationTests
{
    private const string NamespaceName = "mcp-nginx-demo";

    [Fact]
    public async Task ProcessBatchAsync_FailingDeploymentFixture_ProposesRestartAndPublishesProposal()
    {
        await using var gateway = PlannerGatewayFixture.Create();
        await using var mcpClient = await gateway.CreatePlannerClientAsync();

        var batch = CreateBatch(CreateDeploymentUnavailableAnomaly("nginx-demo"));
        var chatClient = CreateRestartDeploymentChatClient("nginx-demo", NamespaceName);
        var sink = new CapturingRemediationProposalSink();

        var processor = CreateProcessor(mcpClient, chatClient, sink);
        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        // exactly one propose_plan call on the stub gateway
        Assert.Contains(gateway.Calls, call => call.ToolName == PlannerConventions.ToolNames.ProposePlan);

        // a batch was published to the sink with the planId returned by the stub
        var published = Assert.Single(sink.Batches);
        Assert.Equal(batch.CycleId, published.CycleId);
        var proposal = Assert.Single(published.Proposals);
        Assert.Equal(PlannerGatewayFixture.FakePlanId, proposal.PlanId);
    }

    [Theory]
    [InlineData(ExecutorDispatchStatuses.Applied, TaskState.Completed)]
    [InlineData(ExecutorDispatchStatuses.Failed, TaskState.Failed)]
    [InlineData(ExecutorDispatchStatuses.Rejected, TaskState.Rejected)]
    public async Task ProcessTaskAsync_FailingDeploymentFixture_MapsExecutorOutcome(
        string executorStatus,
        TaskState expectedTaskState)
    {
        await using var gateway = PlannerGatewayFixture.Create();
        await using var mcpClient = await gateway.CreatePlannerClientAsync();

        var anomaly = CreateDeploymentUnavailableAnomaly("nginx-demo");
        var batch = CreateBatch(anomaly);
        var store = new InMemoryPlannerTaskStore();
        await store.TryCreateTaskAsync(
            "task-1",
            new AgentTask
            {
                Id = "task-1",
                ContextId = anomaly.AnomalyId,
                Status = new A2A.TaskStatus
                {
                    State = TaskState.Submitted,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            },
            CancellationToken.None);
        var lifecycle = new PlannerTaskLifecycle(store, new ChannelEventNotifier());
        var executorDispatchClient = new CapturingExecutorDispatchClient(executorStatus);
        var processor = CreateProcessor(
            mcpClient,
            CreateRestartDeploymentChatClient("nginx-demo", NamespaceName),
            new CapturingRemediationProposalSink(),
            taskLifecycle: lifecycle,
            executorDispatchClient: executorDispatchClient);

        await processor.ProcessTaskAsync(
            new PlannerTaskWorkItem("task-1", anomaly.AnomalyId, batch),
            CancellationToken.None);

        var task = await store.GetTaskAsync("task-1", CancellationToken.None);
        Assert.Equal(expectedTaskState, task!.Status.State);
        Assert.Equal(executorStatus, task.Status.Message!.Parts.Single().Text);
        var artifact = Assert.Single(task.Artifacts!);
        Assert.Equal(PlannerTaskStoreConventions.Artifacts.PlanReferenceId, artifact.ArtifactId);
        Assert.Equal(PlannerGatewayFixture.FakePlanId, Assert.Single(artifact.Parts).Text);
        Assert.Equal(
            (anomaly.AnomalyId, PlannerGatewayFixture.FakePlanId),
            Assert.Single(executorDispatchClient.Calls));
    }

    [Fact]
    public async Task ProcessBatchAsync_AnomalyIdToProposalCorrelation_IsAsserted()
    {
        await using var gateway = PlannerGatewayFixture.Create();
        await using var mcpClient = await gateway.CreatePlannerClientAsync();

        var anomaly = CreateDeploymentUnavailableAnomaly("nginx-demo");
        var batch = CreateBatch(anomaly);
        var chatClient = CreateRestartDeploymentChatClient("nginx-demo", NamespaceName);
        var sink = new CapturingRemediationProposalSink();

        var processor = CreateProcessor(mcpClient, chatClient, sink);
        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        var proposal = Assert.Single(Assert.Single(sink.Batches).Proposals);
        Assert.Equal(anomaly.AnomalyId, proposal.AnomalyId);
    }

    [Fact]
    public async Task ProcessBatchAsync_DuplicateAnomaly_SecondCallSkippedByDedupe()
    {
        await using var gateway = PlannerGatewayFixture.Create();
        await using var mcpClient = await gateway.CreatePlannerClientAsync();

        var anomaly = CreateDeploymentUnavailableAnomaly("nginx-demo");
        var batch = CreateBatch(anomaly);
        var chatClient = CreateRestartDeploymentChatClient("nginx-demo", NamespaceName);
        var sink = new CapturingRemediationProposalSink();
        var dedupeStore = new PlannerDedupeStore();

        var processor = CreateProcessor(mcpClient, chatClient, sink, dedupeStore);

        // First batch: produces a proposal and tracks it in the dedupe store
        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        // Second batch with the same anomaly: dedupe store has an active plan
        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        // propose_plan was called exactly once despite two batch runs
        int proposePlanCalls = gateway.Calls.Count(c => c.ToolName == PlannerConventions.ToolNames.ProposePlan);
        Assert.Equal(1, proposePlanCalls);
        Assert.Single(sink.Batches);
    }

    [Fact]
    public async Task ProcessBatchAsync_ResolvedAnomaly_NotProposed()
    {
        await using var gateway = PlannerGatewayFixture.Create();
        await using var mcpClient = await gateway.CreatePlannerClientAsync();

        var batch = CreateBatch(CreateAnomaly(AnomalyStatus.Resolved, AnomalyKind.DeploymentUnavailable, "nginx-demo"));
        var chatClient = CreateRestartDeploymentChatClient("nginx-demo", NamespaceName);
        var sink = new CapturingRemediationProposalSink();

        var processor = CreateProcessor(mcpClient, chatClient, sink);
        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        Assert.DoesNotContain(gateway.Calls, c => c.ToolName == PlannerConventions.ToolNames.ProposePlan);
        Assert.DoesNotContain(sink.Batches, _ => true);
    }

    [Fact]
    public async Task ProcessBatchAsync_ProposedPlanId_IsNonEmpty()
    {
        await using var gateway = PlannerGatewayFixture.Create();
        await using var mcpClient = await gateway.CreatePlannerClientAsync();

        var batch = CreateBatch(CreateDeploymentUnavailableAnomaly("nginx-demo"));
        var chatClient = CreateRestartDeploymentChatClient("nginx-demo", NamespaceName);
        var sink = new CapturingRemediationProposalSink();

        var processor = CreateProcessor(mcpClient, chatClient, sink);
        await processor.ProcessBatchAsync(batch, CancellationToken.None);

        var proposal = Assert.Single(Assert.Single(sink.Batches).Proposals);
        Assert.False(string.IsNullOrWhiteSpace(proposal.PlanId));
    }

    [Fact]
    public async Task GetAgentToolsAsync_ExcludesProposePlanTool()
    {
        await using var gateway = PlannerGatewayFixture.Create();
        await using var mcpClient = await gateway.CreatePlannerClientAsync();

        var tools = await mcpClient.GetAgentToolsAsync(CancellationToken.None);

        Assert.DoesNotContain(tools, t => t.Name == PlannerConventions.ToolNames.ProposePlan);
        Assert.Contains(tools, t => t.Name == PlannerConventions.ToolNames.GetAllowedNamespaces);
    }

    // --- helpers ---

    private static IPromptLibrary BuildTestPromptLibrary()
    {
        var services = new ServiceCollection();
        services.AddInfraGatePromptLibrary(b => b.AddTemplate(
            PlannerConventions.Prompts.SystemPromptTemplateName,
            "planner test prompt"));
        return services.BuildServiceProvider().GetRequiredService<IPromptLibrary>();
    }

    private static BatchProcessor CreateProcessor(
        IAgentMcpToolset mcpClient,
        FixtureChatClient chatClientFactory,
        IRemediationProposalSink sink,
        PlannerDedupeStore? dedupeStore = null,
        PlannerTaskLifecycle? taskLifecycle = null,
        IExecutorDispatchClient? executorDispatchClient = null)
    {
        var options = new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost/mcp",
            AnomalyWallClockCapSeconds = 30,
            BatchWallClockCapSeconds = 300,
            MaxToolIterations = 4,
        };
        var optionsMonitor = new FixedOptionsMonitor<PlannerOptions>(options);

        return new BatchProcessor(
            optionsMonitor,
            new AnomalyBatchQueue(),
            new ToolCallingAgentFactory(chatClientFactory),
            mcpClient,
            sink,
            NullLogger<BatchProcessor>.Instance,
            BuildTestPromptLibrary(),
            dedupeStore,
            taskLifecycle: taskLifecycle,
            executorDispatchClient: executorDispatchClient);
    }

    private static FixtureChatClient CreateRestartDeploymentChatClient(string name, string ns)
    {
        return new FixtureChatClient($$"""
        {
          "operationType": "restart_deployment",
          "arguments": {
            "name": "{{name}}",
            "namespace": "{{ns}}"
          },
          "reasoning": "Deployment has no available replicas."
        }
        """);
    }

    private static AnomalyHandoffBatch CreateBatch(params AnomalyReport[] reports)
    {
        return new AnomalyHandoffBatch
        {
            CycleId = Guid.NewGuid().ToString(),
            EmittedAt = DateTimeOffset.UtcNow,
            Reports = reports,
        };
    }

    private static AnomalyReport CreateDeploymentUnavailableAnomaly(string name)
    {
        return CreateAnomaly(AnomalyStatus.Active, AnomalyKind.DeploymentUnavailable, name);
    }

    private static AnomalyReport CreateAnomaly(AnomalyStatus status, AnomalyKind kind, string name)
    {
        return new AnomalyReport
        {
            AnomalyId = $"anomaly-{name}-{kind}",
            CycleId = "cycle-integration",
            DetectedAt = DateTimeOffset.UtcNow,
            Kind = kind,
            Target = new ResourceRef
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Namespace = NamespaceName,
                Name = name,
            },
            Severity = Severity.High,
            Status = status,
            Summary = $"Deployment {name} has no available replicas.",
            Evidence = [],
            Annotations = new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    // --- in-process gateway stub ---

    private sealed class PlannerGatewayFixture : IAsyncDisposable
    {
        public const string FakePlanId = "plan-integration-stub";
        private const string McpPath = "/mcp";
        private readonly TestServer server;

        private PlannerGatewayFixture(TestServer server, StubTokenProvider tokenProvider)
        {
            this.server = server;
            TokenProvider = tokenProvider;
        }

        public StubTokenProvider TokenProvider { get; }

        public ConcurrentBag<GatewayToolCall> Calls { get; } = [];

        public static PlannerGatewayFixture Create()
        {
            var tokenProvider = new StubTokenProvider();
            PlannerGatewayFixture? fixture = null;

            var server = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services
                        .AddMcpServer()
                        .WithHttpTransport(opt => { opt.Stateless = true; })
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

                            string text = GetToolResponse(toolName);
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
                            string authorization = context.Request.Headers.Authorization.ToString();
                            if (!string.Equals(authorization, "Bearer planner-token", StringComparison.Ordinal))
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                                return;
                            }
                        }

                        await next(context).ConfigureAwait(false);
                    });
                    app.UseEndpoints(endpoints => endpoints.MapMcp(McpPath));
                }));

            fixture = new PlannerGatewayFixture(server, tokenProvider);
            return fixture;
        }

        public async Task<HttpMcpAgentToolset> CreatePlannerClientAsync()
        {
            var client = new HttpMcpAgentToolset(server, TokenProvider);
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            return client;
        }

        public ValueTask DisposeAsync()
        {
            server.Dispose();
            return ValueTask.CompletedTask;
        }

        private static string GetToolResponse(string toolName)
        {
            return toolName switch
            {
                PlannerConventions.ToolNames.ProposePlan =>
                    $$$"""{"planId":"{{{FakePlanId}}}","accessCodeSent":true,"codeExpiresAt":"2026-12-31T23:59:59Z"}""",
                PlannerConventions.ToolNames.GetAllowedNamespaces =>
                    """{ "namespaces": [ "mcp-nginx-demo" ] }""",
                _ => """{}""",
            };
        }

        private static IList<Tool> CreateTools()
        {
            var schema = JsonSerializer.SerializeToElement(new { type = "object" });
            string[] readOnlyToolNames =
            [
                PlannerConventions.ToolNames.GetAllowedNamespaces, PlannerConventions.ToolNames.GetK8sStatus,
                PlannerConventions.ToolNames.GetK8sEvents, "get_k8s_pods",
                "describe_k8s_resource", "get_k8s_deployments",
                "get_k8s_services", "get_k8s_endpoints",
            ];
            var tools = readOnlyToolNames
                .Select(toolName => new Tool
                {
                    Name = toolName,
                    Description = $"Stubbed read-only tool {toolName}.",
                    InputSchema = schema,
                    Annotations = new ToolAnnotations { ReadOnlyHint = true },
                })
                .ToList<Tool>();
            tools.Add(new Tool
            {
                Name = PlannerConventions.ToolNames.ProposePlan,
                Description = "Stubbed propose_plan tool.",
                InputSchema = schema,
            });
            return tools;
        }
    }

    // Thin IAgentMcpToolset backed by the in-process TestServer.
    private sealed class HttpMcpAgentToolset : IAgentMcpToolset, IAsyncDisposable
    {
        private readonly TestServer server;
        private readonly StubTokenProvider tokenProvider;
        private HttpClient? httpClient;
        private McpClient? mcpClient;

        public HttpMcpAgentToolset(TestServer server, StubTokenProvider tokenProvider)
        {
            this.server = server;
            this.tokenProvider = tokenProvider;
            GatewayBaseUrl = "http://localhost/mcp";
        }

        public string GatewayBaseUrl { get; }

        public bool IsConnected => mcpClient is not null;

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (mcpClient is not null) return;

            var bearerHandler = new ClientCredentialsBearerHandler(
                tokenProvider,
                NullLogger<ClientCredentialsBearerHandler>.Instance)
            {
                InnerHandler = server.CreateHandler()
            };
            httpClient = new HttpClient(bearerHandler) { BaseAddress = new Uri("http://localhost") };

            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(GatewayBaseUrl),
                    Name = "planner-integration-test",
                    TransportMode = HttpTransportMode.StreamableHttp,
                },
                httpClient,
                NullLoggerFactory.Instance,
                ownsHttpClient: false);

            mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<AITool>> GetAgentToolsAsync(CancellationToken cancellationToken)
        {
            if (mcpClient is null)
                throw new InvalidOperationException("Not connected.");
            var allTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return allTools
                .Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true)
                .Cast<AITool>()
                .ToList();
        }

        public async Task<CallToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            if (mcpClient is null)
                throw new InvalidOperationException("Not connected.");

            return await mcpClient.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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

    // Minimal IChatClient/IChatClientFactory fixed response stub (mirrors the unit-test FixtureChatClient).
    private sealed class FixtureChatClient(string textResponse) : IChatClient, IChatClientFactory
    {
        public IChatClient Create() => this;
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, textResponse)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Dispose() { }

        object? IChatClient.GetService(Type serviceType, object? serviceKey) => null;
    }

    // Stub token provider: always returns a fixed bearer token that the stub gateway accepts.
    private sealed class StubTokenProvider : IClientCredentialsTokenProvider
    {
        public int GetTokenCalls { get; private set; }

        public Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            GetTokenCalls++;
            return Task.FromResult("planner-token");
        }

        public Task<string> RefreshTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("planner-token");
        }
    }

    private sealed class GatewayToolCall(string ToolName, IReadOnlyDictionary<string, object?> Arguments)
    {
        public string ToolName { get; } = ToolName;
        public IReadOnlyDictionary<string, object?> Arguments { get; } = Arguments;
    }

    private sealed class CapturingRemediationProposalSink : IRemediationProposalSink
    {
        public List<RemediationProposalBatch> Batches { get; } = [];

        public Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingExecutorDispatchClient(string status) : IExecutorDispatchClient
    {
        public List<(string ContextId, string PlanId)> Calls { get; } = [];

        public Task<ExecutorDispatchResult> DispatchAsync(
            string contextId,
            string planId,
            CancellationToken cancellationToken)
        {
            Calls.Add((contextId, planId));
            return Task.FromResult(new ExecutorDispatchResult { Status = status, Detail = status });
        }
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
