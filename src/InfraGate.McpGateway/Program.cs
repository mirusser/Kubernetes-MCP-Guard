using InfraGate.Approvals.Postgres;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.Endpoints;
using InfraGate.McpGateway.Notifications;
using ModelContextProtocol.Protocol;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
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
        transportOptions.Stateless = true;
    })
    .WithListToolsHandler((request, ct) =>
        new ValueTask<ListToolsResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>()
            .ListToolsAsync(request.Params, ct)))
    .WithCallToolHandler((request, ct) =>
        new ValueTask<CallToolResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>()
            .CallToolAsync(request.Params, ct)))
    .WithListResourceTemplatesHandler((request, ct) =>
        new ValueTask<ListResourceTemplatesResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>()
            .ListTemplates()))
    .WithReadResourceHandler((request, ct) =>
        new ValueTask<ReadResourceResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>()
            .ReadAsync(request.Params, ct)))
    .WithSubscriptionsListenHandler((request, ct) =>
        request.Services!.GetRequiredService<PlanStatusSubscriptionsListenHandler>()
            .ListenAsync(request, ct));
WebApplication app = builder.Build();

await builder.Configuration.RunPostgresMigrationsAsync(app).ConfigureAwait(false);

await app.Services.GetRequiredService<PostgresApprovalSchemaValidator>()
    .ValidateAsync(CancellationToken.None).ConfigureAwait(false);

app.UseAuthentication();
app.UseAuthorization();
app.MapGatewayHealthEndpoints();
app.MapGatewayApprovalEndpoints();
app.MapMcp(McpGatewayConventions.McpPath)
    .RequireAuthorization(GatewayAuthConventions.Schemes.PolicyName);

await app.RunAsync().ConfigureAwait(false);
