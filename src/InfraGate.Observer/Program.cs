using System.Diagnostics.Metrics;
using System.Reflection;
using A2A;
using InfraGate.AgentGuardrails;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using InfraGate.AgentLlm;
using InfraGate.AgentMcp;
using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using InfraGate.ClientCredentials;
using InfraGate.Observability;
using InfraGate.Observer;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Classification;
using InfraGate.Observer.Cycle;
using InfraGate.Observer.Diagnostics;
using InfraGate.Observer.Endpoints;
using InfraGate.Observer.Handoff;
using InfraGate.Observer.Llm;
using InfraGate.Observer.Snapshot;
using InfraGate.Observer.State;
using InfraGate.Prompts;
using ModelContextProtocol.Protocol;
using Npgsql;
using Microsoft.Agents.AI.A2A;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ObserverOptions>(
    builder.Configuration.GetSection(ObserverOptions.SectionName));

builder.AddInfraGateObservability(opt =>
{
    opt.WriteToConsole = true;
    opt.ConsoleToStandardError = false;
});

builder.AddInfraGateTelemetry(opt =>
{
    opt.ServiceName = "infragate-observer";
    opt.MeterNames = [ObserverMetrics.MeterName, AgentGuardrailConventions.MeterName];
});

ConfigureUrls(builder);

var observerOptions = builder.Configuration
    .GetSection(ObserverOptions.SectionName)
    .Get<ObserverOptions>() ?? new ObserverOptions();

try
{
    observerOptions.Validate();
}
catch (InvalidOperationException ex)
{
    await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
    return 1;
}

// ClientCredentials binds recursively from InfraGate:Observer:ClientCredentials — no manual mapping.
// AddClientCredentialsTokenProvider validates the bound options (Authority/ClientId/Scope) at startup.
builder.Services.AddClientCredentialsTokenProvider(observerOptions.ClientCredentials);

builder.Services.AddHttpClient(ObserverConventions.HttpClients.PlannerHandoff)
    .AddClientCredentialsBearerHandler();

var jwtAuthority = observerOptions.ClientCredentials.Authority;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwtAuthority;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudiences =
            [
                ObserverConventions.ServiceClients.Planner,
                observerOptions.ClientCredentials.ClientId,
                "account" // Standard Keycloak token audience
            ]
        };
    });

builder.Services
    .AddAuthorizationBuilder()
    .AddPolicy(ObserverConventions.Policies.PlannerSender, policy =>
        policy
            .RequireAuthenticatedUser()
            .RequireClaim(ObserverConventions.Claims.AuthorizedParty, ObserverConventions.ServiceClients.Planner));

builder.Services.AddInfraGateAgentMcp(new AgentMcpOptions
{
    GatewayBaseUrl = observerOptions.GatewayBaseUrl,
    ClientName = ObserverConventions.DefaultClientId,
});
builder.Services.AddSingleton<ISnapshotFetcher>(sp =>
{
    return new SnapshotFetcher(
        sp.GetRequiredService<IAgentMcpToolset>(),
        sp.GetRequiredService<ILogger<SnapshotFetcher>>(),
        ObserverMetrics.Meter);
});
var observerAssembly = typeof(ObservationCycleRunner).Assembly;
var observerPromptTemplate = await LoadEmbeddedResourceAsync(
    observerAssembly, ObserverConventions.Prompts.SystemPromptResourceName).ConfigureAwait(false);
builder.Services.AddInfraGatePromptLibrary(b => b.AddTemplate(
    ObserverConventions.Prompts.SystemPromptTemplateName,
    observerPromptTemplate,
    [ObserverConventions.Prompts.NamespaceArgumentName, ObserverConventions.Prompts.MaxToolIterationsArgumentName]));
builder.Services.AddSingleton<ISeverityClassifier, SeverityClassifier>();
builder.Services.AddSingleton<IChatClientFactory>(sp =>
{
    return new ChatClientFactory(
        sp.GetRequiredService<IOptions<ObserverOptions>>(),
        sp.GetRequiredService<ILoggerFactory>());
});
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var factory = sp.GetRequiredService<IChatClientFactory>();
    return factory.Create();
});
builder.Services.AddSingleton<IAnomalyDedupeStore, AnomalyDedupeStore>();
builder.Services.AddSingleton<ToolCallingAgentFactory>();
builder.Services.AddAgentGuardrails();
builder.Services.AddSingleton(_ =>
{
    var allowedTools = new HashSet<string>(StringComparer.Ordinal)
    {
        ObserverConventions.ToolNames.GetAllowedNamespaces,
        ObserverConventions.ToolNames.GetK8sStatus,
        ObserverConventions.ToolNames.GetK8sEvents,
        ObserverConventions.ToolNames.GetPodLogs,
        ObserverConventions.ToolNames.GetK8sResource,
        ObserverConventions.ToolNames.GetDeploymentDiagnostics,
        ObserverConventions.ToolNames.GetPodDiagnostics,
        ObserverConventions.ToolNames.GetServiceDiagnostics,
    };
    return new AgentGuardrailPolicy(allowedTools);
});
builder.Services.AddSingleton<IObservationCycleRunner>(sp =>
{
    return new ObservationCycleRunner(
        sp.GetRequiredService<IOptionsMonitor<ObserverOptions>>(),
        sp.GetRequiredService<ISnapshotFetcher>(),
        sp.GetRequiredService<IPromptLibrary>(),
        sp.GetRequiredService<ToolCallingAgentFactory>(),
        sp.GetRequiredService<ISeverityClassifier>(),
        sp.GetRequiredService<IAgentMcpToolset>(),
        sp.GetRequiredService<IAnomalyDedupeStore>(),
        sp.GetRequiredService<IAnomalyHandoffSink>(),
        sp.GetRequiredService<ILogger<ObservationCycleRunner>>(),
        ObserverMetrics.Meter,
        sp.GetService<IObserverAuditOutbox>(),
        sp.GetService<AgentGuardrailPolicy>());
});
builder.Services.AddSingleton<CycleSerialisation>();
builder.Services.AddHostedService<ObservationCycleLoop>();

builder.Services.AddSingleton<LoggingAnomalyHandoffSink>();
builder.Services.AddSingleton<IAnomalyHandoffSink>(sp =>
{
    var sinks = new List<IAnomalyHandoffSink>
    {
        sp.GetRequiredService<LoggingAnomalyHandoffSink>(),
    };

    var options = sp.GetRequiredService<IOptions<ObserverOptions>>().Value;

    if (!string.IsNullOrEmpty(options.FileSinkRoot))
    {
        sinks.Add(new JsonFileAnomalyHandoffSink(
            options.FileSinkRoot,
            sp.GetRequiredService<ILogger<JsonFileAnomalyHandoffSink>>()));
    }

    if (!string.IsNullOrEmpty(options.PlannerHandoffUrl))
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(ObserverConventions.HttpClients.PlannerHandoff);
        var a2aLogger = sp.GetRequiredService<ILogger<A2AAnomalyHandoffSink>>();
#pragma warning disable MEAI001 // Experimental A2A preview package — accepted per plan
        var a2aAgent = new A2AAgent(
            new A2AClient(new Uri(options.PlannerHandoffUrl), httpClient),
            name: ObserverConventions.A2AHandoffAgentName);
#pragma warning restore MEAI001
        sinks.Add(new A2AAnomalyHandoffSink(
            new A2APlannerHandoffClient(a2aAgent),
            a2aLogger,
            sp.GetService<IObserverAuditOutbox>()));
    }

    var logger = sp.GetRequiredService<ILogger<CompositeAnomalyHandoffSink>>();
    return new CompositeAnomalyHandoffSink(sinks, logger);
});

builder.Services.AddSingleton<ObserverInboundAgentHandler>();
#pragma warning disable MEAI001 // Experimental A2A preview package — accepted per plan
builder.Services.AddKeyedSingleton<A2AServer>(ObserverConventions.A2AInboundAgentName, (sp, _) =>
{
    var handler = sp.GetRequiredService<ObserverInboundAgentHandler>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new A2AServer(
        handler,
        new InMemoryTaskStore(),
        new ChannelEventNotifier(),
        loggerFactory.CreateLogger<A2AServer>());
});
#pragma warning restore MEAI001

string? auditConnectionString = observerOptions.AuditConnectionString.Length > 0 ? observerOptions.AuditConnectionString : null;
if (!string.IsNullOrWhiteSpace(auditConnectionString))
{
    var auditDataSource = NpgsqlDataSource.Create(auditConnectionString);
    string migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");
    await PostgresAuditOutboxMigrationRunner.ApplyAsync(
        auditDataSource, AuditOutboxConventions.Streams.Observer, migrationsDir, CancellationToken.None).ConfigureAwait(false);
    builder.Services.AddObserverAuditOutbox(auditDataSource);
}

var app = builder.Build();

await ConnectObserverMcpClientAsync(app, observerOptions).ConfigureAwait(false);

app.UseAuthentication();
app.UseAuthorization();

app.MapObserverHealthEndpoint();
app.MapObserverObserveNowEndpoint();
#pragma warning disable MEAI001 // Experimental A2A preview package — accepted per plan
app.MapA2AJsonRpc(ObserverConventions.A2AInboundAgentName, ObserverConventions.A2AInboundEndpointPath)
   .RequireAuthorization(ObserverConventions.Policies.PlannerSender);
#pragma warning restore MEAI001
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        context.Response.Redirect(ObserverConventions.HealthEndpointPath);
        return;
    }
    await next(context).ConfigureAwait(false);
});

await app.RunAsync().ConfigureAwait(false);

return 0;

static void ConfigureUrls(WebApplicationBuilder builder)
{
    string? configuredUrls = builder.Configuration[ObserverConventions.AspNetCoreUrlsKey];
    if (!string.IsNullOrWhiteSpace(configuredUrls))
    {
        builder.WebHost.UseUrls(configuredUrls);
    }
    else
    {
        builder.WebHost.UseUrls(ObserverConventions.DefaultUrl);
    }
}

static async Task<string> LoadEmbeddedResourceAsync(Assembly assembly, string resourceName, CancellationToken cancellationToken = default)
{
    using var stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
}

static async Task ConnectObserverMcpClientAsync(WebApplication app, ObserverOptions options)
{
    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("InfraGate.Observer.Startup");
    var mcpClient = app.Services.GetRequiredService<IAgentMcpToolset>();

    try
    {
        await mcpClient.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        var nsResult = await mcpClient.CallToolAsync(
            ObserverConventions.ToolNames.GetAllowedNamespaces,
            null,
            CancellationToken.None).ConfigureAwait(false);
        string allowedNsResponse = nsResult.IsError != true
            ? string.Join(Environment.NewLine, nsResult.Content.OfType<TextContentBlock>().Select(c => c.Text))
            : string.Empty;

        ObserverLogEvents.LogStartupConnected(
            logger,
            mcpClient.GatewayBaseUrl,
            allowedNsResponse);
    }
    catch (Exception ex)
    {
        var authority = string.IsNullOrEmpty(options.ClientCredentials.Authority) ? "(not set)" : options.ClientCredentials.Authority;
        var scope = options.ClientCredentials.Scope;
        var clientId = options.ClientCredentials.ClientId;

        ObserverLogEvents.LogStartupConnectionFailed(
            logger,
            mcpClient.GatewayBaseUrl,
            authority,
            scope,
            clientId,
            ex);
    }
}
