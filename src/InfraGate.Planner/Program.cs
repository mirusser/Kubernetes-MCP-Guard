using A2A;
using InfraGate.Planner;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Endpoints;
using InfraGate.Planner.Handoff;
using InfraGate.Planner.Tasks;
using InfraGate.AgentGuardrails;
using InfraGate.AgentLlm;
using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using InfraGate.Planner.Llm;
using InfraGate.AgentMcp;
using InfraGate.Prompts;
using ModelContextProtocol.Protocol;
using InfraGate.Observability;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Agents.AI.A2A;
using Microsoft.Extensions.AI;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PlannerOptions>(
    builder.Configuration.GetSection(PlannerOptions.SectionName));

builder.AddInfraGateObservability(opt =>
{
    opt.WriteToConsole = true;
    opt.ConsoleToStandardError = false;
});

builder.AddInfraGateTelemetry(opt =>
{
    opt.ServiceName = "infragate-planner";
    opt.MeterNames = [PlannerMetrics.MeterName, AgentGuardrailConventions.MeterName];
});

ConfigureUrls(builder);

var plannerOptions = builder.Configuration
    .GetSection(PlannerOptions.SectionName)
    .Get<PlannerOptions>() ?? new PlannerOptions();

try
{
    plannerOptions.Validate();
}
catch (InvalidOperationException ex)
{
    await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
    return 1;
}

// ClientCredentials binds recursively from InfraGate:Planner:ClientCredentials — no manual mapping.
// AddClientCredentialsTokenProvider validates the bound options (Authority/ClientId/Scope) at startup.
builder.Services.AddClientCredentialsTokenProvider(plannerOptions.ClientCredentials);

builder.Services.AddInfraGateAgentMcp(new AgentMcpOptions
{
    GatewayBaseUrl = plannerOptions.GatewayBaseUrl,
    ClientName = PlannerConventions.DefaultClientId,
});
builder.Services.AddSingleton<IChatClientFactory>(sp =>
{
    return new ChatClientFactory(
        sp.GetRequiredService<IOptions<PlannerOptions>>(),
        sp.GetRequiredService<ILoggerFactory>());
});
builder.Services.AddSingleton<ToolCallingAgentFactory>();
builder.Services.AddAgentGuardrails();
builder.Services.AddSingleton(_ =>
{
    var allowedTools = new HashSet<string>(StringComparer.Ordinal)
    {
        PlannerConventions.ToolNames.GetAllowedNamespaces,
        PlannerConventions.ToolNames.GetK8sStatus,
        PlannerConventions.ToolNames.GetK8sEvents,
        PlannerConventions.ToolNames.GetK8sPods,
        PlannerConventions.ToolNames.DescribeK8sResource,
        PlannerConventions.ToolNames.GetK8sDeployments,
        PlannerConventions.ToolNames.GetK8sServices,
        PlannerConventions.ToolNames.GetK8sEndpoints,
    };
    return new AgentGuardrailPolicy(allowedTools);
});
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var factory = sp.GetRequiredService<IChatClientFactory>();
    return factory.Create();
});
var plannerAssembly = typeof(BatchProcessor).Assembly;
var plannerPromptTemplate = await LoadEmbeddedResourceAsync(
    plannerAssembly, PlannerConventions.Prompts.SystemPromptResourceName).ConfigureAwait(false);
builder.Services.AddInfraGatePromptLibrary(b => b.AddTemplate(
    PlannerConventions.Prompts.SystemPromptTemplateName,
    plannerPromptTemplate));

builder.Services.AddSingleton<AnomalyBatchQueue>();
builder.Services.AddSingleton<PlannerDedupeStore>();
builder.Services.AddHttpClient(PlannerConventions.HttpClients.ExecutorHandoff, httpClient =>
    {
        httpClient.Timeout = PlannerConventions.ExecutorDispatchTimeout;
    })
    .AddClientCredentialsBearerHandler();
if (!string.IsNullOrEmpty(plannerOptions.ExecutorHandoffUrl))
{
    builder.Services.AddSingleton<IExecutorDispatchClient>(sp =>
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(PlannerConventions.HttpClients.ExecutorHandoff);
#pragma warning disable MEAI001 // Experimental A2A preview package - accepted per plan
        var agent = new A2AAgent(
            new A2AClient(new Uri(plannerOptions.ExecutorHandoffUrl), httpClient),
            name: PlannerConventions.A2AExecutorAgentName);
#pragma warning restore MEAI001
        return new A2AExecutorDispatchClient(agent, sp.GetRequiredService<ILogger<A2AExecutorDispatchClient>>());
    });
}

builder.Services.AddHttpClient(PlannerConventions.HttpClients.ObserverRequest)
    .AddClientCredentialsBearerHandler();

builder.Services.AddSingleton<LoggingRemediationProposalSink>();
builder.Services.AddSingleton<IRemediationProposalSink>(sp =>
{
    var sinks = new List<IRemediationProposalSink>
    {
        sp.GetRequiredService<LoggingRemediationProposalSink>(),
    };

    var options = sp.GetRequiredService<IOptions<PlannerOptions>>().Value;

    if (!string.IsNullOrEmpty(options.FileSinkRoot))
    {
        sinks.Add(new JsonFileRemediationProposalSink(
            options.FileSinkRoot,
            sp.GetRequiredService<ILogger<JsonFileRemediationProposalSink>>()));
    }

    var logger = sp.GetRequiredService<ILogger<CompositeRemediationProposalSink>>();
    return new CompositeRemediationProposalSink(sinks, logger);
});
string? auditConnectionString = plannerOptions.AuditConnectionString.Length > 0 ? plannerOptions.AuditConnectionString : null;
IPlannerTaskStore plannerTaskStore = new InMemoryPlannerTaskStore();
if (!string.IsNullOrWhiteSpace(auditConnectionString))
{
    var auditDataSource = NpgsqlDataSource.Create(auditConnectionString);
    string migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");
    await PostgresAuditOutboxMigrationRunner.ApplyAsync(
        auditDataSource, AuditOutboxConventions.Streams.Planner, migrationsDir, CancellationToken.None).ConfigureAwait(false);
    builder.Services.AddPlannerAuditOutbox(auditDataSource);

    // Durable A2A task store on the same Postgres data source - lets the Planner reconcile
    // in-flight (waiting) remediation tasks after a restart.
    string taskMigrationsDir = Path.Combine(AppContext.BaseDirectory, PlannerTaskStoreConventions.MigrationsRelativePath);
    await PostgresAuditOutboxMigrationRunner.ApplyAsync(
        auditDataSource, PlannerTaskStoreConventions.Schema, taskMigrationsDir, CancellationToken.None).ConfigureAwait(false);
    plannerTaskStore = new PostgresTaskStore(auditDataSource);
}
builder.Services.AddSingleton(plannerTaskStore);
builder.Services.AddSingleton<ChannelEventNotifier>();
builder.Services.AddSingleton<PlannerTaskLifecycle>();
builder.Services.AddHostedService<PlannerTaskReconciler>();

if (!string.IsNullOrEmpty(plannerOptions.ObserverBaseUrl))
{
    builder.Services.AddSingleton<IObserverChannel>(sp =>
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(PlannerConventions.HttpClients.ObserverRequest);
#pragma warning disable MEAI001 // Experimental A2A preview package — accepted per plan
        var agent = new A2AClient(new Uri(plannerOptions.ObserverBaseUrl), httpClient)
            .AsAIAgent(name: PlannerConventions.A2AObserverAgentName);
#pragma warning restore MEAI001
        return new ObserverChannel(agent, sp.GetRequiredService<ILogger<ObserverChannel>>());
    });
}

builder.Services.AddSingleton<BatchProcessor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BatchProcessor>());

builder.Services.AddSingleton<PlannerHandoffAgentHandler>();
#pragma warning disable MEAI001 // Experimental A2A preview package — accepted per plan
builder.Services.AddKeyedSingleton<A2AServer>(PlannerConventions.A2AHandoffAgentName, (sp, _) =>
{
    var handler = sp.GetRequiredService<PlannerHandoffAgentHandler>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new A2AServer(
        handler,
        plannerTaskStore,
        sp.GetRequiredService<ChannelEventNotifier>(),
        loggerFactory.CreateLogger<A2AServer>());
});
#pragma warning restore MEAI001

var jwtAuthority = plannerOptions.ClientCredentials.Authority;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwtAuthority;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = false;
        // CA5404: suppressed — Planner is not a registered resource server; the Observer's tokens
        // carry the gateway as their audience. Security is enforced by the ObserverSender policy
        // which checks azp == infra-gate-observer on every inbound request.
#pragma warning disable CA5404
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateAudience = false,
        };
#pragma warning restore CA5404
    });

builder.Services
    .AddAuthorizationBuilder()
    .AddPolicy(PlannerConventions.Policies.ObserverSender, policy =>
        policy
            .RequireAuthenticatedUser()
            .RequireClaim(PlannerConventions.Claims.AuthorizedParty, PlannerConventions.ServiceClients.Observer));

var app = builder.Build();

await ConnectPlannerMcpClientAsync(app, plannerOptions).ConfigureAwait(false);

app.UseAuthentication();
app.UseAuthorization();

app.MapPlannerHealthEndpoint();
#pragma warning disable MEAI001 // Experimental A2A preview package — accepted per plan
app.MapA2AJsonRpc(PlannerConventions.A2AHandoffAgentName, PlannerConventions.A2AHandoffEndpointPath)
   .RequireAuthorization(PlannerConventions.Policies.ObserverSender);
#pragma warning restore MEAI001
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        context.Response.Redirect(PlannerConventions.HealthEndpointPath);
        return;
    }
    await next(context).ConfigureAwait(false);
});

await app.RunAsync().ConfigureAwait(false);

return 0;

static void ConfigureUrls(WebApplicationBuilder builder)
{
    string? configuredUrls = builder.Configuration[PlannerConventions.AspNetCoreUrlsKey];
    if (!string.IsNullOrWhiteSpace(configuredUrls))
    {
        builder.WebHost.UseUrls(configuredUrls);
    }
    else
    {
        builder.WebHost.UseUrls(PlannerConventions.DefaultUrl);
    }
}

static async Task<string> LoadEmbeddedResourceAsync(Assembly assembly, string resourceName, CancellationToken cancellationToken = default)
{
    using var stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
}

static async Task ConnectPlannerMcpClientAsync(WebApplication app, PlannerOptions options)
{
    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("InfraGate.Planner.Startup");
    var mcpClient = app.Services.GetRequiredService<IAgentMcpToolset>();

    try
    {
        await mcpClient.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        var nsResult = await mcpClient.CallToolAsync(
            PlannerConventions.ToolNames.GetAllowedNamespaces,
            null,
            CancellationToken.None).ConfigureAwait(false);
        string allowedNsResponse = nsResult.IsError != true
            ? string.Join(Environment.NewLine, nsResult.Content.OfType<TextContentBlock>().Select(c => c.Text))
            : string.Empty;

        PlannerLogEvents.LogStartupConnected(
            logger,
            mcpClient.GatewayBaseUrl,
            allowedNsResponse);
    }
    catch (Exception ex)
    {
        var authority = string.IsNullOrEmpty(options.ClientCredentials.Authority) ? "(not set)" : options.ClientCredentials.Authority;
        var scope = options.ClientCredentials.Scope;
        var clientId = options.ClientCredentials.ClientId;

        PlannerLogEvents.LogStartupConnectionFailed(
            logger,
            mcpClient.GatewayBaseUrl,
            authority,
            scope,
            clientId,
            ex);
    }
}
