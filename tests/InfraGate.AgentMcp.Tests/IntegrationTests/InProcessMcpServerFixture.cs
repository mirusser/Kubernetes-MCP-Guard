// ASPDEPR004/ASPDEPR008: WebHostBuilder + TestServer are deprecated in favor of WebApplicationBuilder.
// Suppressed because: integration tests need an in-process MCP endpoint without binding a real port.
#pragma warning disable ASPDEPR004
#pragma warning disable ASPDEPR008

namespace InfraGate.AgentMcp.Tests.IntegrationTests;

internal sealed class InProcessMcpServerFixture : IAsyncDisposable
{
    public const string McpPath = "/mcp";

    // Profiled primary diagnostic read (DiagnosticCapabilityProfile), correct name and schema.
    public const string ReadOnlyToolName = "get_k8s_status";

    // Profiled secondary diagnostic read (DiagnosticCapabilityProfile), correct name and schema.
    public const string SecondaryReadOnlyToolName = "pods_get";

    // ReadOnlyHint=true but not a name DiagnosticCapabilityProfile recognizes at all — the
    // "unknown/unprofiled" adversarial case.
    public const string UnprofiledReadOnlyToolName = "get_k8s_pods";

    // A profiled name (get_k8s_resource) whose declared schema no longer matches the pinned
    // property set — the "schema-drifted" adversarial case.
    public const string SchemaDriftedToolName = "get_k8s_resource";

    // Not ReadOnlyHint=true at all — the "destructive/mutation" case.
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
                    .WithHttpTransport(o => { o.Stateless = true; })
                    .WithListToolsHandler((_, _) =>
                        new ValueTask<ListToolsResult>(new ListToolsResult { Tools = CreateTools() }))
                    .WithCallToolHandler((request, _) =>
                    {
                        string toolName = request.Params?.Name
                            ?? throw new InvalidOperationException("MCP call missing tool name.");
                        string text = toolName switch
                        {
                            ReadOnlyToolName
                                or SecondaryReadOnlyToolName
                                or UnprofiledReadOnlyToolName
                                or SchemaDriftedToolName => ReadOnlyToolResponse,
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
        var emptySchema = JsonSerializer.SerializeToElement(new { type = "object" });
        var statusSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { @namespace = new { type = "string" }, labelSelector = new { type = "string" } },
        });
        var podsGetSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { @namespace = new { type = "string" }, name = new { type = "string" } },
            required = new[] { "name" },
        });
        // Drifted: real get_k8s_resource takes {namespace, kind, name}; this schema is missing
        // "kind" and adds an unreviewed "unexpectedParam" instead.
        var driftedSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { @namespace = new { type = "string" }, unexpectedParam = new { type = "string" } },
        });

        return
        [
            new Tool
            {
                Name = ReadOnlyToolName,
                Description = "Read-only: get k8s status.",
                InputSchema = statusSchema,
                Annotations = new ToolAnnotations { ReadOnlyHint = true },
            },
            new Tool
            {
                Name = SecondaryReadOnlyToolName,
                Description = "Read-only: get a pod (kubernetes-mcp-server).",
                InputSchema = podsGetSchema,
                Annotations = new ToolAnnotations { ReadOnlyHint = true },
            },
            new Tool
            {
                Name = UnprofiledReadOnlyToolName,
                Description = "Read-only: get k8s pods (not in the diagnostic profile).",
                InputSchema = emptySchema,
                Annotations = new ToolAnnotations { ReadOnlyHint = true },
            },
            new Tool
            {
                Name = SchemaDriftedToolName,
                Description = "Read-only: get k8s resource, with a drifted schema.",
                InputSchema = driftedSchema,
                Annotations = new ToolAnnotations { ReadOnlyHint = true },
            },
            new Tool
            {
                Name = MutationToolName,
                Description = "Mutation: propose a plan.",
                InputSchema = emptySchema,
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
