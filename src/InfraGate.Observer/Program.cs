using InfraGate.ClientCredentials;
using InfraGate.Observer;
using InfraGate.Observer.Classification;
using InfraGate.Observer.Cycle;
using InfraGate.Observer.Endpoints;
using InfraGate.Observer.Llm;
using InfraGate.Observer.Mcp;
using InfraGate.Observer.Prompts;
using InfraGate.Observer.Snapshot;
using InfraGate.Observability;
using InfraGate.RuntimeSafety;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInfraGateEnvironmentVariables(mappings =>
{
    mappings.Map(ObserverConventions.EnvironmentVariables.GatewayBaseUrl, ObserverConventions.ConfigurationKeys.GatewayBaseUrl);
    mappings.Map(ObserverConventions.EnvironmentVariables.CycleIntervalSeconds, ObserverConventions.ConfigurationKeys.CycleIntervalSeconds);
    mappings.Map(ObserverConventions.EnvironmentVariables.WallClockCapSeconds, ObserverConventions.ConfigurationKeys.WallClockCapSeconds);
    mappings.Map(ObserverConventions.EnvironmentVariables.MaxToolIterations, ObserverConventions.ConfigurationKeys.MaxToolIterations);
    mappings.Map(ObserverConventions.EnvironmentVariables.AllowedNamespaces, ObserverConventions.ConfigurationKeys.AllowedNamespaces);
    mappings.Map(ObserverConventions.EnvironmentVariables.LlmProvider, ObserverConventions.ConfigurationKeys.LlmProvider);
    mappings.Map(ObserverConventions.EnvironmentVariables.LlmModel, ObserverConventions.ConfigurationKeys.LlmModel);
    mappings.Map(ObserverConventions.EnvironmentVariables.LlmApiKey, ObserverConventions.ConfigurationKeys.LlmApiKey);
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

observerOptions.Validate();

var authOptions = new ClientCredentialsTokenOptions
{
    Authority = builder.Configuration[ObserverConventions.EnvironmentVariables.OAuthAuthority] ?? string.Empty,
    ClientId = builder.Configuration[ObserverConventions.EnvironmentVariables.ClientId] ?? ObserverConventions.DefaultClientId,
    ClientSecret = builder.Configuration[ObserverConventions.EnvironmentVariables.ClientSecret],
    Scope = builder.Configuration[ObserverConventions.EnvironmentVariables.OAuthScope] ?? ObserverConventions.DefaultOAuthScope,
    RequireHttpsMetadata = false,
};
builder.Services.AddClientCredentialsTokenProvider(authOptions);

builder.Services.AddSingleton<IObserverMcpClient, ObserverMcpClient>();
builder.Services.AddSingleton<ISnapshotFetcher, SnapshotFetcher>();
builder.Services.AddSingleton<ISystemPromptProvider, SystemPromptProvider>();
builder.Services.AddSingleton<ISeverityClassifier, SeverityClassifier>();
builder.Services.AddSingleton<IChatClientFactory, ChatClientFactory>();
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var factory = sp.GetRequiredService<IChatClientFactory>();
    return factory.Create();
});
builder.Services.AddSingleton<IObservationCycleRunner, ObservationCycleRunner>();
builder.Services.AddHostedService<ObservationCycleLoop>();

var app = builder.Build();

await ConnectObserverMcpClientAsync(app).ConfigureAwait(false);

app.MapObserverHealthEndpoint();
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

        logger.LogInformation(
            "observer.startup.connected Gateway={GatewayBaseUrl} AllowedNamespaces={AllowedNamespaces}",
            mcpClient.GatewayBaseUrl,
            allowedNsResponse);
    }
    catch (Exception ex)
    {
        var authority = configuration[ObserverConventions.EnvironmentVariables.OAuthAuthority] ?? "(not set)";
        var scope = configuration[ObserverConventions.EnvironmentVariables.OAuthScope] ?? ObserverConventions.DefaultOAuthScope;
        var clientId = configuration[ObserverConventions.EnvironmentVariables.ClientId] ?? ObserverConventions.DefaultClientId;

        logger.LogWarning(
            ex,
            "observer.startup.connection_failed Gateway={GatewayBaseUrl} Authority={Authority} Scope={Scope} ClientId={ClientId}",
            mcpClient.GatewayBaseUrl,
            authority,
            scope,
            clientId);
    }
}
