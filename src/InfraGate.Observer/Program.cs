using System.Diagnostics.Metrics;
using InfraGate.ClientCredentials;
using InfraGate.Observer;
using InfraGate.Observer.Audit;
using InfraGate.Observer.Classification;
using InfraGate.Observer.Cycle;
using InfraGate.Observer.Diagnostics;
using InfraGate.Observer.Endpoints;
using InfraGate.Observer.Handoff;
using InfraGate.AgentLlm;
using InfraGate.AuditOutbox.Postgres;
using InfraGate.Observer.Llm;
using InfraGate.Observer.Mcp;
using InfraGate.Observer.Prompts;
using InfraGate.Observer.Snapshot;
using InfraGate.Observer.State;
using InfraGate.Observability;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;
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

builder.Services.AddSingleton<IObserverMcpClient, ObserverMcpClient>();
builder.Services.AddSingleton<ISnapshotFetcher>(sp =>
{
    return new SnapshotFetcher(
        sp.GetRequiredService<IObserverMcpClient>(),
        sp.GetRequiredService<ILogger<SnapshotFetcher>>(),
        ObserverMetrics.Meter);
});
builder.Services.AddSingleton<ISystemPromptProvider, SystemPromptProvider>();
builder.Services.AddSingleton<ISeverityClassifier, SeverityClassifier>();
builder.Services.AddSingleton<IChatClientFactory>(sp =>
{
    return new ChatClientFactory(
        sp.GetRequiredService<IOptions<ObserverOptions>>(),
        ObserverMetrics.Meter);
});
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var factory = sp.GetRequiredService<IChatClientFactory>();
    return factory.Create();
});
builder.Services.AddSingleton<IAnomalyDedupeStore, AnomalyDedupeStore>();
builder.Services.AddSingleton<IObservationCycleRunner>(sp =>
{
    return new ObservationCycleRunner(
        sp.GetRequiredService<IOptionsMonitor<ObserverOptions>>(),
        sp.GetRequiredService<ISnapshotFetcher>(),
        sp.GetRequiredService<ISystemPromptProvider>(),
        sp.GetRequiredService<IChatClient>(),
        sp.GetRequiredService<ISeverityClassifier>(),
        sp.GetRequiredService<IObserverMcpClient>(),
        sp.GetRequiredService<IAnomalyDedupeStore>(),
        sp.GetRequiredService<IAnomalyHandoffSink>(),
        sp.GetRequiredService<ILogger<ObservationCycleRunner>>(),
        ObserverMetrics.Meter,
        sp.GetService<IObserverAuditOutbox>());
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

var auditConnectionString = builder.Configuration[ObserverConventions.ConfigurationKeys.AuditConnectionString];
if (!string.IsNullOrWhiteSpace(auditConnectionString))
{
    var auditDataSource = NpgsqlDataSource.Create(auditConnectionString);
    var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");
    await PostgresAuditOutboxMigrationRunner.ApplyAsync(
        auditDataSource, "observer", migrationsDir, CancellationToken.None).ConfigureAwait(false);
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

static async Task ConnectObserverMcpClientAsync(WebApplication app)
{
    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("InfraGate.Observer.Startup");
    var mcpClient = app.Services.GetRequiredService<IObserverMcpClient>();
    var configuration = app.Services.GetRequiredService<IConfiguration>();

    try
    {
        await mcpClient.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        var allowedNsResponse = await mcpClient.GetToolResultAsync(
            ObserverConventions.ToolNames.GetAllowedNamespaces,
            null,
            CancellationToken.None).ConfigureAwait(false);

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
