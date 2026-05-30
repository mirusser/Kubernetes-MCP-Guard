using System.Diagnostics.Metrics;
using System.Reflection;
using InfraGate.AgentGuardrails;
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
using InfraGate.RuntimeSafety;
using ModelContextProtocol.Protocol;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInfraGateEnvironmentVariables(mappings =>
{
    mappings.Map(ObserverConventions.EnvironmentVariables.GatewayBaseUrl, ObserverConventions.ConfigurationKeys.GatewayBaseUrl);
    mappings.Map(ObserverConventions.EnvironmentVariables.CycleIntervalSeconds, ObserverConventions.ConfigurationKeys.CycleIntervalSeconds);
    mappings.Map(ObserverConventions.EnvironmentVariables.WallClockCapSeconds, ObserverConventions.ConfigurationKeys.WallClockCapSeconds);
    mappings.Map(ObserverConventions.EnvironmentVariables.MaxToolIterations, ObserverConventions.ConfigurationKeys.MaxToolIterations);
    mappings.MapList(ObserverConventions.EnvironmentVariables.AllowedNamespaces, ObserverConventions.ConfigurationKeys.AllowedNamespaces);
    mappings.Map(ObserverConventions.EnvironmentVariables.LlmProvider, ObserverConventions.ConfigurationKeys.LlmProvider);
    mappings.Map(ObserverConventions.EnvironmentVariables.LlmModel, ObserverConventions.ConfigurationKeys.LlmModel);
    mappings.Map(ObserverConventions.EnvironmentVariables.LlmApiKey, ObserverConventions.ConfigurationKeys.LlmApiKey);
    mappings.Map(ObserverConventions.EnvironmentVariables.DedupeSuppressionWindow, ObserverConventions.ConfigurationKeys.DedupeSuppressionWindow);
    mappings.Map(ObserverConventions.EnvironmentVariables.DedupeResolutionThreshold, ObserverConventions.ConfigurationKeys.DedupeResolutionThreshold);
    mappings.Map(ObserverConventions.EnvironmentVariables.FileSinkRoot, ObserverConventions.ConfigurationKeys.FileSinkRoot);
    mappings.Map(ObserverConventions.EnvironmentVariables.PlannerHandoffUrl, ObserverConventions.ConfigurationKeys.PlannerHandoffUrl);
    mappings.Map(ObserverConventions.EnvironmentVariables.AuditConnectionString, ObserverConventions.ConfigurationKeys.AuditConnectionString);
    RuntimeSafetyConventions.RegisterInfraGateEnvVarMappings(mappings);
});

builder.Services.Configure<ObserverOptions>(
    builder.Configuration.GetSection(ObserverConventions.ConfigurationKeys.Observer));

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
    .GetSection(ObserverConventions.ConfigurationKeys.Observer)
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

var authOptions = new ClientCredentialsTokenOptions
{
    Authority = builder.Configuration[ObserverConventions.EnvironmentVariables.OAuthAuthority] ?? string.Empty,
    ClientId = builder.Configuration[ObserverConventions.EnvironmentVariables.ClientId] ?? ObserverConventions.DefaultClientId,
    ClientSecret = builder.Configuration[ObserverConventions.EnvironmentVariables.ClientSecret],
    Scope = builder.Configuration[ObserverConventions.EnvironmentVariables.OAuthScope] ?? ObserverConventions.DefaultOAuthScope,
    RequireHttpsMetadata = false,
};
builder.Services.AddClientCredentialsTokenProvider(authOptions);

builder.Services.AddHttpClient(ObserverConventions.HttpClients.PlannerHandoff)
    .AddClientCredentialsBearerHandler();

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
        ObserverConventions.ToolNames.GetK8sPods,
        ObserverConventions.ToolNames.DescribeK8sResource,
        ObserverConventions.ToolNames.GetK8sDeployments,
        ObserverConventions.ToolNames.GetK8sServices,
        ObserverConventions.ToolNames.GetK8sEndpoints,
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
        var httpLogger = sp.GetRequiredService<ILogger<HttpAnomalyHandoffSink>>();
        sinks.Add(new HttpAnomalyHandoffSink(httpClient, options.PlannerHandoffUrl, httpLogger, sp.GetService<IObserverAuditOutbox>()));
    }

    var logger = sp.GetRequiredService<ILogger<CompositeAnomalyHandoffSink>>();
    return new CompositeAnomalyHandoffSink(sinks, logger);
});

string? auditConnectionString = builder.Configuration[ObserverConventions.ConfigurationKeys.AuditConnectionString];
if (!string.IsNullOrWhiteSpace(auditConnectionString))
{
    var auditDataSource = NpgsqlDataSource.Create(auditConnectionString);
    string migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");
    await PostgresAuditOutboxMigrationRunner.ApplyAsync(
        auditDataSource, AuditOutboxConventions.Streams.Observer, migrationsDir, CancellationToken.None).ConfigureAwait(false);
    builder.Services.AddObserverAuditOutbox(auditDataSource);
}

var app = builder.Build();

await ConnectObserverMcpClientAsync(app).ConfigureAwait(false);

app.MapObserverHealthEndpoint();
app.MapObserverObserveNowEndpoint();
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
    string? configuredUrls = builder.Configuration[ObserverConventions.EnvironmentVariables.AspNetCoreUrls];
    if (string.IsNullOrWhiteSpace(configuredUrls))
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

static async Task ConnectObserverMcpClientAsync(WebApplication app)
{
    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("InfraGate.Observer.Startup");
    var mcpClient = app.Services.GetRequiredService<IAgentMcpToolset>();
    var configuration = app.Services.GetRequiredService<IConfiguration>();

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
        var authority = configuration[ObserverConventions.EnvironmentVariables.OAuthAuthority] ?? "(not set)";
        var scope = configuration[ObserverConventions.EnvironmentVariables.OAuthScope] ?? ObserverConventions.DefaultOAuthScope;
        var clientId = configuration[ObserverConventions.EnvironmentVariables.ClientId] ?? ObserverConventions.DefaultClientId;

        ObserverLogEvents.LogStartupConnectionFailed(
            logger,
            mcpClient.GatewayBaseUrl,
            authority,
            scope,
            clientId,
            ex);
    }
}
