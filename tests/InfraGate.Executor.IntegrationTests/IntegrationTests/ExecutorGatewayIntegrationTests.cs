// ASPDEPR004/ASPDEPR008: WebHostBuilder + TestServer are deprecated in favor of WebApplicationBuilder.
// Suppressed because: these integration tests need an in-process MCP gateway endpoint without binding a real port.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using InfraGate.ClientCredentials;
using InfraGate.Executor.Diagnostics;
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

namespace InfraGate.Executor.IntegrationTests.IntegrationTests;

public sealed class ExecutorGatewayIntegrationTests
{
    // --- Task 9.6 tracer bullets ---

    [Fact]
    public async Task WatchPlanAsync_ApprovedOnFirstPoll_CallsExecuteApprovedPlan()
    {
        await using var gateway = ExecutorGatewayFixture.Create();
        gateway.SetWaitResponse(ExecutorConventions.PlanStatusValues.Approved);
        gateway.SetExecuteResponse(success: true);
        await using var mcpClient = await gateway.CreateExecutorClientAsync();

        var proposal = CreateProposal("plan-approved");
        var watcher = CreateWatcher(mcpClient);

        await watcher.WatchPlanAsync(proposal, CancellationToken.None);

        Assert.Contains(gateway.Calls, c => c.ToolName == ExecutorConventions.ToolNames.WaitForPlanApproval);
        Assert.Contains(gateway.Calls, c => c.ToolName == ExecutorConventions.ToolNames.ExecuteApprovedPlan);
    }

    [Fact]
    public async Task WatchPlanAsync_ApprovedPlan_PassesPlanIdToExecute()
    {
        await using var gateway = ExecutorGatewayFixture.Create();
        gateway.SetWaitResponse(ExecutorConventions.PlanStatusValues.Approved);
        gateway.SetExecuteResponse(success: true);
        await using var mcpClient = await gateway.CreateExecutorClientAsync();

        var proposal = CreateProposal("plan-id-check");
        var watcher = CreateWatcher(mcpClient);

        await watcher.WatchPlanAsync(proposal, CancellationToken.None);

        var executeCall = Assert.Single(gateway.Calls, c => c.ToolName == ExecutorConventions.ToolNames.ExecuteApprovedPlan);
        Assert.True(executeCall.Arguments.TryGetValue(ExecutorConventions.ToolArguments.PlanId, out var planId));
        string? planIdValue = planId is System.Text.Json.JsonElement el ? el.GetString() : planId as string;
        Assert.Equal("plan-id-check", planIdValue);
    }

    [Fact]
    public async Task WatchPlanAsync_DuplicatePlanId_ExecutedOnlyOnce()
    {
        await using var gateway = ExecutorGatewayFixture.Create();
        gateway.SetWaitResponse(ExecutorConventions.PlanStatusValues.Approved);
        gateway.SetExecuteResponse(success: true);
        await using var mcpClient = await gateway.CreateExecutorClientAsync();

        var proposal = CreateProposal("plan-dedupe");
        var watcher = CreateWatcher(mcpClient);

        // First watch: runs normally
        await watcher.WatchPlanAsync(proposal, CancellationToken.None);

        // Re-track to simulate a second arrival of the same planId before dedupe expires
        // (TryTrack should return false the second time while the plan is still tracked)
        var dedupeStore = new ExecutorDedupeStore();
        dedupeStore.TryTrack("plan-dedupe"); // mark it active
        var watcher2 = CreateWatcher(mcpClient, dedupeStore);

        await watcher2.WatchPlanAsync(proposal, CancellationToken.None);

        // execute_approved_plan should only have been called once (first watcher only)
        int executeCalls = gateway.Calls.Count(c => c.ToolName == ExecutorConventions.ToolNames.ExecuteApprovedPlan);
        Assert.Equal(1, executeCalls);
    }

    [Fact]
    public async Task WatchPlanAsync_NotFoundResponse_DoesNotCallExecute()
    {
        await using var gateway = ExecutorGatewayFixture.Create();
        gateway.SetWaitResponse(ExecutorConventions.PlanStatusValues.NotFound);
        await using var mcpClient = await gateway.CreateExecutorClientAsync();

        var proposal = CreateProposal("plan-notfound");
        var watcher = CreateWatcher(mcpClient);

        await watcher.WatchPlanAsync(proposal, CancellationToken.None);

        Assert.DoesNotContain(gateway.Calls, c => c.ToolName == ExecutorConventions.ToolNames.ExecuteApprovedPlan);
    }

    [Fact]
    public async Task WatchPlanAsync_ErrorResponseFromExecute_DoesNotThrow()
    {
        await using var gateway = ExecutorGatewayFixture.Create();
        gateway.SetWaitResponse(ExecutorConventions.PlanStatusValues.Approved);
        gateway.SetExecuteResponse(success: false);
        await using var mcpClient = await gateway.CreateExecutorClientAsync();

        var proposal = CreateProposal("plan-execute-err");
        var watcher = CreateWatcher(mcpClient);

        // Must not throw — blocked execution is swallowed and logged
        var ex = await Record.ExceptionAsync(() => watcher.WatchPlanAsync(proposal, CancellationToken.None));
        Assert.Null(ex);

        Assert.Contains(gateway.Calls, c => c.ToolName == ExecutorConventions.ToolNames.ExecuteApprovedPlan);
    }

    // --- helpers ---

    private static PlanWatcher CreateWatcher(
        IExecutorMcpClient mcpClient,
        IExecutorDedupeStore? dedupeStore = null)
    {
        var options = new ExecutorOptions
        {
            WatchTimeoutSeconds = 120,
            ConcurrencyCap = 8,
        };
        var optionsMonitor = new FixedOptionsMonitor<ExecutorOptions>(options);
        return new PlanWatcher(
            dedupeStore ?? new ExecutorDedupeStore(),
            mcpClient,
            optionsMonitor,
            NullLogger<PlanWatcher>.Instance);
    }

    private static RemediationProposal CreateProposal(string planId)
    {
        return new RemediationProposal
        {
            PlanId = planId,
            AnomalyId = $"anomaly-{planId}",
            ProposedAt = DateTimeOffset.UtcNow,
        };
    }

    // --- in-process gateway stub ---

    private sealed class ExecutorGatewayFixture : IAsyncDisposable
    {
        private const string McpPath = "/mcp";
        private readonly TestServer server;
        private string waitStatus = ExecutorConventions.PlanStatusValues.Approved;
        private bool executeSuccess = true;

        private ExecutorGatewayFixture(TestServer server, StubTokenProvider tokenProvider)
        {
            this.server = server;
            TokenProvider = tokenProvider;
        }

        public StubTokenProvider TokenProvider { get; }

        public ConcurrentBag<GatewayToolCall> Calls { get; } = [];

        public void SetWaitResponse(string status) => waitStatus = status;

        public void SetExecuteResponse(bool success) => executeSuccess = success;

        public static ExecutorGatewayFixture Create()
        {
            var tokenProvider = new StubTokenProvider();
            ExecutorGatewayFixture? fixture = null;

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
                            string authorization = context.Request.Headers.Authorization.ToString();
                            if (!string.Equals(authorization, "Bearer executor-token", StringComparison.Ordinal))
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                                return;
                            }
                        }

                        await next(context).ConfigureAwait(false);
                    });
                    app.UseEndpoints(endpoints => endpoints.MapMcp(McpPath));
                }));

            fixture = new ExecutorGatewayFixture(server, tokenProvider);
            return fixture;
        }

        public async Task<HttpMcpExecutorClient> CreateExecutorClientAsync()
        {
            var client = new HttpMcpExecutorClient(server, TokenProvider);
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            return client;
        }

        public ValueTask DisposeAsync()
        {
            server.Dispose();
            return ValueTask.CompletedTask;
        }

        private string GetToolResponse(string toolName)
        {
            return toolName switch
            {
                ExecutorConventions.ToolNames.WaitForPlanApproval =>
                    $$$"""{"status":"{{{waitStatus}}}","timedOut":false}""",
                ExecutorConventions.ToolNames.ExecuteApprovedPlan =>
                    executeSuccess
                        ? """{"status":"Applied"}"""
                        : """{"isError":true,"content":[{"text":"Pre-execution gate rejected."}]}""",
                _ => """{}""",
            };
        }

        private static IList<Tool> CreateTools()
        {
            var schema = JsonSerializer.SerializeToElement(new { type = "object" });
            return ExecutorConventions.ToolNames.AllowedToolNames
                .Select(toolName => new Tool
                {
                    Name = toolName,
                    Description = $"Stubbed tool {toolName}.",
                    InputSchema = schema,
                })
                .ToList();
        }
    }

    // Thin MCP client that drives ExecutorMcpClient against the in-process TestServer.
    private sealed class HttpMcpExecutorClient : IExecutorMcpClient, IAsyncDisposable
    {
        private readonly TestServer server;
        private readonly StubTokenProvider tokenProvider;
        private McpClient? mcpClient;

        public HttpMcpExecutorClient(TestServer server, StubTokenProvider tokenProvider)
        {
            this.server = server;
            this.tokenProvider = tokenProvider;
            GatewayBaseUrl = "http://localhost/mcp";
        }

        public string GatewayBaseUrl { get; }

        public bool IsConnected => mcpClient is not null;

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            var httpClient = ExecutorMcpClient.CreateHttpClient(
                GatewayBaseUrl,
                tokenProvider,
                NullLoggerFactory.Instance,
                server.CreateHandler());

            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(GatewayBaseUrl),
                    Name = "executor-integration-test",
                },
                httpClient,
                NullLoggerFactory.Instance,
                ownsHttpClient: true);

            mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<string> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments,
            CancellationToken cancellationToken)
        {
            ExecutorToolWhitelist.AssertAllowed(toolName);

            if (mcpClient is null)
            {
                throw new InvalidOperationException("Not connected.");
            }

            var result = await mcpClient.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return JsonSerializer.Serialize(result);
        }

        public async ValueTask DisposeAsync()
        {
            if (mcpClient is not null)
            {
                await mcpClient.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class StubTokenProvider : IClientCredentialsTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken)
            => Task.FromResult("executor-token");

        public Task<string> RefreshTokenAsync(CancellationToken cancellationToken)
            => Task.FromResult("executor-token");
    }

    private sealed class GatewayToolCall(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        public string ToolName { get; } = toolName;
        public IReadOnlyDictionary<string, object?> Arguments { get; } = arguments;
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
