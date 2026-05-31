// ASPDEPR004/ASPDEPR008: WebHostBuilder + TestServer are deprecated in favor of WebApplicationBuilder.
// Suppressed because: integration tests need an in-process MCP endpoint without binding a real port.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.AgentMcp.Tests.IntegrationTests;

internal sealed class InProcessMcpServerFixture : IAsyncDisposable
{
    public const string McpPath = "/mcp";

    public const string ReadOnlyToolName = "get_k8s_status";
    public const string AnotherReadOnlyToolName = "get_k8s_pods";
    public const string MutationToolName = "propose_plan";
    public const string ReadOnlyToolResponse = """{"status": "healthy"}""";
    public const string MutationToolResponse = """{"planId": "plan-abc-123"}""";

    private readonly TestServer server;

    private InProcessMcpServerFixture(TestServer server, StubTokenProvider tokenProvider)
    {
        this.server = server;
        TokenProvider = tokenProvider;
    }

    public StubTokenProvider TokenProvider { get; }

    public static InProcessMcpServerFixture Create()
    {
        var tokenProvider = new StubTokenProvider();
        var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services
                    .AddMcpServer()
                    .WithHttpTransport(o => { o.Stateless = false; })
                    .WithListToolsHandler((_, _) =>
                        new ValueTask<ListToolsResult>(new ListToolsResult { Tools = CreateTools() }))
                    .WithCallToolHandler((request, _) =>
                    {
                        string toolName = request.Params?.Name
                            ?? throw new InvalidOperationException("MCP call missing tool name.");
                        string text = toolName switch
                        {
                            ReadOnlyToolName or AnotherReadOnlyToolName => ReadOnlyToolResponse,
                            MutationToolName => MutationToolResponse,
                            _ => throw new InvalidOperationException($"Unexpected tool '{toolName}'."),
                        };
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
                        if (!string.Equals(authorization, "Bearer test-token", StringComparison.Ordinal))
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                            return;
                        }
                    }

                    await next(context).ConfigureAwait(false);
                });
                app.UseEndpoints(endpoints => endpoints.MapMcp(McpPath));
            }));

        return new InProcessMcpServerFixture(server, tokenProvider);
    }

    public async Task<AgentMcpToolset> CreateToolsetAsync()
    {
        var options = new AgentMcpOptions
        {
            GatewayBaseUrl = new Uri(server.BaseAddress, McpPath).ToString(),
            ClientName = "test-agent",
        };

        var bearerHandler = new ClientCredentialsBearerHandler(
            TokenProvider,
            NullLogger<ClientCredentialsBearerHandler>.Instance)
        {
            InnerHandler = server.CreateHandler()
        };
        var httpClient = new HttpClient(bearerHandler)
        {
            BaseAddress = server.BaseAddress
        };

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress, McpPath),
                Name = "test-agent",
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: true);

        var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);

        return AgentMcpToolset.CreateFromClient(mcpClient, options);
    }

    public ValueTask DisposeAsync()
    {
        server.Dispose();
        return ValueTask.CompletedTask;
    }

    private static IList<Tool> CreateTools()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        return
        [
            new Tool
            {
                Name = ReadOnlyToolName,
                Description = "Read-only: get k8s status.",
                InputSchema = schema,
                Annotations = new ToolAnnotations { ReadOnlyHint = true },
            },
            new Tool
            {
                Name = AnotherReadOnlyToolName,
                Description = "Read-only: get k8s pods.",
                InputSchema = schema,
                Annotations = new ToolAnnotations { ReadOnlyHint = true },
            },
            new Tool
            {
                Name = MutationToolName,
                Description = "Mutation: propose a plan.",
                InputSchema = schema,
            },
        ];
    }
}

internal sealed class StubTokenProvider : IClientCredentialsTokenProvider
{
    private int getTokenCalls;

    public int GetTokenCalls => getTokenCalls;

    public Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref getTokenCalls);
        return Task.FromResult("test-token");
    }

    public Task<string> RefreshTokenAsync(CancellationToken cancellationToken) =>
        Task.FromResult("test-token");
}
