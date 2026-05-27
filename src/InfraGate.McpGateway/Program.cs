using InfraGate.Approvals.Postgres;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Notifications;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);

GatewayConfigurationExtensions.AddInfraGateConfiguration(builder.Configuration, args);
builder.AddInfraGateServices();

builder.Services
    .AddMcpServer(serverOptions =>
    {
        serverOptions.Capabilities = new ServerCapabilities
        {
            Resources = new ResourcesCapability { Subscribe = true }
        };
        serverOptions.ServerInstructions = """
            Approval workflow (MANDATORY — no exceptions):
            1. After calling any request_* tool, call execute_approved_plan(planId=...) to get the approval URL.
            2. You MUST then call wait_for_plan_approval(planId=...) in a polling loop (55 s timeout, repeat as needed).
                Do NOT wait for the user to confirm approval — poll automatically.
            3. When wait_for_plan_approval returns Approved, call execute_approved_plan again to apply the plan.
            Skipping the polling step and waiting for user confirmation instead is not permitted.
            """;
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
            catch (OperationCanceledException)
            {
                handlerLogger.LogInformation("RunSessionHandler: session cancelled (client disconnected)"); // NOSONAR
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
        new ValueTask<ListToolsResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>().ListToolsAsync(request.Params, ct)))
    .WithCallToolHandler((request, ct) =>
    {
        if (request.Services!.GetService<IHttpContextAccessor>() is { HttpContext: { } httpCtx })
        {
            httpCtx.Items[NotificationsConventions.McpSessionIdItemKey] = request.Server.SessionId;
        }
        return new ValueTask<CallToolResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>().CallToolAsync(request.Params, ct));
    })
    .WithListResourceTemplatesHandler((request, ct) =>
        new ValueTask<ListResourceTemplatesResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>().ListTemplates()))
    .WithReadResourceHandler((request, ct) =>
        new ValueTask<ReadResourceResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>().ReadAsync(request.Params, ct)))
    .WithSubscribeToResourcesHandler((request, ct) =>
        new ValueTask<EmptyResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>().Subscribe(request.Server.SessionId, request.Params)))
    .WithUnsubscribeFromResourcesHandler((request, ct) =>
        new ValueTask<EmptyResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>().Unsubscribe(request.Server.SessionId, request.Params)));

var app = builder.Build();

await GatewayConfigurationExtensions.RunPostgresMigrationsAsync(
    builder.Configuration, app).ConfigureAwait(false);

await app.Services.GetRequiredService<PostgresApprovalSchemaValidator>()
    .ValidateAsync(CancellationToken.None).ConfigureAwait(false);

app.UseAuthentication();
app.UseAuthorization();
app.MapGatewayApprovalEndpoints();
app.MapMcp(McpGatewayConventions.McpPath)
    .RequireAuthorization(GatewayAuthConventions.Schemes.PolicyName);

await app.RunAsync().ConfigureAwait(false);
