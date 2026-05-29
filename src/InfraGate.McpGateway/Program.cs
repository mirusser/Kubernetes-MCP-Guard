using InfraGate.Approvals.Postgres;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Notifications;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInfraGateConfiguration(args);
    builder.AddInfraGateServices();

    builder.Services
        .AddMcpServer(serverOptions =>
        {
            serverOptions.Capabilities = new ServerCapabilities
            {
                Resources = new ResourcesCapability { Subscribe = true }
            };
            serverOptions.ServerInstructions = ServerInstructions.ApprovalWorkflow;
        })
        .WithHttpTransport(transportOptions =>
        {
            transportOptions.Stateless = false;

#pragma warning disable MCPEXP002
            transportOptions.RunSessionHandler = async (httpContext, server, ct) =>
            {
                var handlerLogger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("InfraGate.McpGateway.SessionHandler");
                handlerLogger.LogInformation("RunSessionHandler: started (session={SessionId})", server.SessionId);

                var registry = httpContext.RequestServices.GetRequiredService<ISubscriptionRegistry>();
                var id = server.SessionId;
                if (id is not null)
                {
                    registry.RegisterSession(id, new McpServerSessionNotifier(server));
                }

                try
                {
                    handlerLogger.LogInformation("RunSessionHandler: calling server.RunAsync");
                    await server.RunAsync(ct).ConfigureAwait(false);
                    handlerLogger.LogInformation("RunSessionHandler: server.RunAsync completed normally");
                }
                catch (OperationCanceledException ex)
                {
                    handlerLogger.LogInformation(ex,
                        "RunSessionHandler: session cancelled (client disconnected)"); // NOSONAR
                }
                catch (Exception ex)
                {
                    handlerLogger.LogError(ex, "RunSessionHandler: unexpected exception");
                }
                finally
                {
                    if (id is not null)
                    {
                        registry.RemoveSession(id);
                    }

                    handlerLogger.LogInformation("RunSessionHandler: cleanup done (session={SessionId})", id);
                }
            };
#pragma warning restore MCPEXP002
        })
        .WithListToolsHandler((request, ct) =>
            new ValueTask<ListToolsResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>()
                .ListToolsAsync(request.Params, ct)))
        .WithCallToolHandler((request, ct) =>
        {
            if (request.Services!.GetService<IHttpContextAccessor>() is { HttpContext: { } httpCtx })
            {
                httpCtx.Items[NotificationsConventions.McpSessionIdItemKey] = request.Server.SessionId;
            }

            return new ValueTask<CallToolResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>()
                .CallToolAsync(request.Params, ct));
        })
        .WithListResourceTemplatesHandler((request, ct) =>
            new ValueTask<ListResourceTemplatesResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>()
                .ListTemplates()))
        .WithReadResourceHandler((request, ct) =>
            new ValueTask<ReadResourceResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>()
                .ReadAsync(request.Params, ct)))
        .WithSubscribeToResourcesHandler((request, ct) =>
            new ValueTask<EmptyResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>()
                .Subscribe(request.Server.SessionId, request.Params)))
        .WithUnsubscribeFromResourcesHandler((request, ct) =>
            new ValueTask<EmptyResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>()
                .Unsubscribe(request.Server.SessionId, request.Params)));
var app = builder.Build();

await builder.Configuration.RunPostgresMigrationsAsync(app).ConfigureAwait(false);

await app.Services.GetRequiredService<PostgresApprovalSchemaValidator>()
    .ValidateAsync(CancellationToken.None).ConfigureAwait(false);

app.UseAuthentication();
app.UseAuthorization();
app.MapGatewayApprovalEndpoints();
app.MapMcp(McpGatewayConventions.McpPath)
    .RequireAuthorization(GatewayAuthConventions.Schemes.PolicyName);

await app.RunAsync().ConfigureAwait(false);