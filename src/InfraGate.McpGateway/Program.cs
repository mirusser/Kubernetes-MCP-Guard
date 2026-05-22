using InfraGate.Approvals;
using InfraGate.Approvals.Postgres;
using InfraGate.KubernetesAdapter;
using InfraGate.McpGateway;
using InfraGate.McpGateway.Auth;
using InfraGate.McpGateway.DownstreamAuth;
using InfraGate.McpGateway.Notifications;
using InfraGate.Observability;
using InfraGate.RuntimeSafety;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
GatewayConfigurationExtensions.AddInfraGateConfiguration(builder.Configuration, args);

builder.Services.Configure<InfraGateGatewaySettings>(
    builder.Configuration.GetSection("InfraGate:Gateway"));
builder.Services.Configure<InfraGateAuthSettings>(
    builder.Configuration.GetSection("InfraGate:Auth"));
builder.Services.Configure<InfraGateApprovalSettings>(
    builder.Configuration.GetSection("InfraGate:Approval"));

var options = McpGatewayOptions.FromConfiguration(builder.Configuration);
options.ValidateProductionSafety();

builder.AddInfraGateObservability(opt => 
{
    opt.WriteToConsole = true;
    opt.ConsoleToStandardError = false;
});

if (string.IsNullOrWhiteSpace(builder.Configuration[McpGatewayConventions.ConfigurationKeys.Urls]) &&
    string.IsNullOrWhiteSpace(builder.Configuration[McpGatewayConventions.EnvironmentVariables.AspNetCoreUrls]))
{
    string configuredUrls = builder.Configuration[McpGatewayConventions.ConfigurationKeys.AspNetCoreUrls] ??
        McpGatewayOptions.DefaultUrl;
    builder.WebHost.UseUrls(configuredUrls);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
        Path.GetDirectoryName(options.ApprovalRoot)!,
        ApprovalConventions.Storage.DataProtectionKeysDirectory)))
    .SetApplicationName(ApprovalConventions.Application.Name);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IGuardrailAuditStore, GuardrailAuditStore>();
builder.Services.AddSingleton<IDownstreamMcpClient, DownstreamMcpClient>();
builder.Services.AddSingleton<GuardedToolRunner>();
builder.Services.AddPostgresApprovalPersistence(
    builder.Configuration[McpGatewayConventions.ConfigurationKeys.ApprovalPostgresConnectionString]);
builder.Services.AddSingleton<IAuthorizationCheck, SameSubjectAuthorizationCheck>();
builder.Services.AddSingleton<IGatewayApprovalService, GatewayApprovalService>();
builder.Services.AddSingleton<IApprovalPreExecutionGate, ApprovalPreExecutionGate>();
builder.Services.AddSingleton<IToolCaller>(sp => (IToolCaller)sp.GetRequiredService<IDownstreamMcpClient>());
builder.Services.AddKubernetesAdapter();
builder.Services.AddSingleton<DownstreamToolRegistry>();
builder.Services.AddSingleton<IGatewayToolDispatcher, GatewayToolDispatcher>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery();
builder.Services.AddGatewayAuthentication(options.Auth);

var downstreamAuth = options.DownstreamAuth ?? new InfraGate.DownstreamAuth.DownstreamAuthOptions();
if (downstreamAuth.Required)
{
    builder.Services.AddSingleton(downstreamAuth);
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<IDownstreamServiceTokenProvider>(sp =>
        new ClientCredentialsDownstreamServiceTokenProvider(
            sp.GetRequiredService<InfraGate.DownstreamAuth.DownstreamAuthOptions>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ClientCredentialsDownstreamServiceTokenProvider)),
            TimeProvider.System,
            sp.GetRequiredService<ILogger<ClientCredentialsDownstreamServiceTokenProvider>>()));
}
else
{
    builder.Services.AddSingleton<IDownstreamServiceTokenProvider, NullDownstreamServiceTokenProvider>();
}

builder.Services.AddSingleton<ISubscriptionRegistry, SubscriptionRegistry>();
builder.Services.AddSingleton<IApprovalNotificationDispatcher, ApprovalNotificationDispatcher>();
builder.Services.AddSingleton<PlanStatusResourceHandler>();

builder.Services
    .AddMcpServer(serverOptions =>
    {
        serverOptions.Capabilities = new ServerCapabilities
        {
            Resources = new ResourcesCapability { Subscribe = true }
        };
    })
    .WithHttpTransport(transportOptions =>
    {
        transportOptions.Stateless = false;

        // RunSessionHandler is experimental in ModelContextProtocol.AspNetCore 1.3.0.
        // Calling server.RunAsync(ct) manually starts the session message loop.
        // Task.Delay(Timeout.Infinite) was incorrect — it kept the handler alive but never
        // started the session, so no MCP messages (including initialize) were ever processed.
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
                handlerLogger.LogInformation("RunSessionHandler: session cancelled (client disconnected)");
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
    .WithListToolsHandler((RequestContext<ListToolsRequestParams> request, CancellationToken ct) =>
        new ValueTask<ListToolsResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>().ListToolsAsync(request.Params, ct)))
    .WithCallToolHandler((RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
    {
        // Store session ID per-request so the dispatcher can retrieve it without a scoped dependency.
        if (request.Services!.GetService<IHttpContextAccessor>() is { HttpContext: { } httpCtx })
        {
            httpCtx.Items[NotificationsConventions.McpSessionIdItemKey] = request.Server.SessionId;
        }
        return new ValueTask<CallToolResult>(request.Services!.GetRequiredService<IGatewayToolDispatcher>().CallToolAsync(request.Params, ct));
    })
    .WithListResourceTemplatesHandler((RequestContext<ListResourceTemplatesRequestParams> request, CancellationToken ct) =>
        new ValueTask<ListResourceTemplatesResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>().ListTemplates()))
    .WithReadResourceHandler((RequestContext<ReadResourceRequestParams> request, CancellationToken ct) =>
        new ValueTask<ReadResourceResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>().ReadAsync(request.Params, ct)))
    .WithSubscribeToResourcesHandler((RequestContext<SubscribeRequestParams> request, CancellationToken ct) =>
        new ValueTask<EmptyResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>().Subscribe(request.Server.SessionId, request.Params)))
    .WithUnsubscribeFromResourcesHandler((RequestContext<UnsubscribeRequestParams> request, CancellationToken ct) =>
        new ValueTask<EmptyResult>(request.Services!.GetRequiredService<PlanStatusResourceHandler>().Unsubscribe(request.Server.SessionId, request.Params)));

var app = builder.Build();

await app.Services.GetRequiredService<PostgresApprovalSchemaValidator>()
    .ValidateAsync(CancellationToken.None).ConfigureAwait(false);

app.UseAuthentication();
app.UseAuthorization();
app.MapGatewayApprovalEndpoints();
app.MapMcp(McpGatewayConventions.McpPath)
    .RequireAuthorization(GatewayAuthConventions.Schemes.PolicyName);

await app.RunAsync().ConfigureAwait(false);
