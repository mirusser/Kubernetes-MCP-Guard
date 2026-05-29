using InfraGate.Planner;
using InfraGate.Planner.Audit;
using InfraGate.Planner.Cycle;
using InfraGate.Planner.Dedupe;
using InfraGate.Planner.Diagnostics;
using InfraGate.Planner.Endpoints;
using InfraGate.Planner.Handoff;
using InfraGate.AgentLlm;
using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using InfraGate.Planner.Llm;
using InfraGate.Planner.Mcp;
using InfraGate.Observability;
using InfraGate.RuntimeSafety;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInfraGateEnvironmentVariables(mappings =>
{
    mappings.Map(PlannerConventions.EnvironmentVariables.GatewayBaseUrl, PlannerConventions.ConfigurationKeys.GatewayBaseUrl);
    mappings.Map(PlannerConventions.EnvironmentVariables.ExecutorHandoffUrl, PlannerConventions.ConfigurationKeys.ExecutorHandoffUrl);
    mappings.Map(PlannerConventions.EnvironmentVariables.AnomalyWallClockCapSeconds, PlannerConventions.ConfigurationKeys.AnomalyWallClockCapSeconds);
    mappings.Map(PlannerConventions.EnvironmentVariables.BatchWallClockCapSeconds, PlannerConventions.ConfigurationKeys.BatchWallClockCapSeconds);
    mappings.Map(PlannerConventions.EnvironmentVariables.MaxToolIterations, PlannerConventions.ConfigurationKeys.MaxToolIterations);
    mappings.Map(PlannerConventions.EnvironmentVariables.LlmProvider, PlannerConventions.ConfigurationKeys.LlmProvider);
    mappings.Map(PlannerConventions.EnvironmentVariables.LlmModel, PlannerConventions.ConfigurationKeys.LlmModel);
    mappings.Map(PlannerConventions.EnvironmentVariables.LlmApiKey, PlannerConventions.ConfigurationKeys.LlmApiKey);
    mappings.Map(PlannerConventions.EnvironmentVariables.FileSinkRoot, PlannerConventions.ConfigurationKeys.FileSinkRoot);
    mappings.Map(PlannerConventions.EnvironmentVariables.AuditConnectionString, PlannerConventions.ConfigurationKeys.AuditConnectionString);
    RuntimeSafetyConventions.RegisterInfraGateEnvVarMappings(mappings);
});

builder.Services.Configure<PlannerOptions>(
    builder.Configuration.GetSection(PlannerConventions.ConfigurationKeys.Planner));

builder.AddInfraGateObservability(opt =>
{
    opt.WriteToConsole = true;
    opt.ConsoleToStandardError = false;
});

ConfigureUrls(builder);

var plannerOptions = builder.Configuration
    .GetSection(PlannerConventions.ConfigurationKeys.Planner)
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

var authOptions = new ClientCredentialsTokenOptions
{
    Authority = builder.Configuration[PlannerConventions.EnvironmentVariables.OAuthAuthority] ?? string.Empty,
    ClientId = builder.Configuration[PlannerConventions.EnvironmentVariables.ClientId] ?? PlannerConventions.DefaultClientId,
    ClientSecret = builder.Configuration[PlannerConventions.EnvironmentVariables.ClientSecret],
    Scope = builder.Configuration[PlannerConventions.EnvironmentVariables.OAuthScope] ?? PlannerConventions.DefaultOAuthScope,
    RequireHttpsMetadata = false,
};
builder.Services.AddClientCredentialsTokenProvider(authOptions);

builder.Services.AddSingleton<IPlannerMcpClient, PlannerMcpClient>();
builder.Services.AddSingleton<IChatClientFactory>(sp =>
{
    return new ChatClientFactory(
        sp.GetRequiredService<IOptions<PlannerOptions>>(),
        PlannerMetrics.Meter,
        sp.GetRequiredService<ILoggerFactory>());
});
builder.Services.AddSingleton<ToolCallingAgentFactory>();
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var factory = sp.GetRequiredService<IChatClientFactory>();
    return factory.Create();
});
builder.Services.AddSingleton<AnomalyBatchQueue>();
builder.Services.AddSingleton<PlannerDedupeStore>();
builder.Services.AddHttpClient(PlannerConventions.HttpClients.ExecutorHandoff)
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

    if (!string.IsNullOrEmpty(options.ExecutorHandoffUrl))
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(PlannerConventions.HttpClients.ExecutorHandoff);
        var httpLogger = sp.GetRequiredService<ILogger<HttpRemediationProposalSink>>();
        sinks.Add(new HttpRemediationProposalSink(httpClient, options.ExecutorHandoffUrl, httpLogger));
    }

    var logger = sp.GetRequiredService<ILogger<CompositeRemediationProposalSink>>();
    return new CompositeRemediationProposalSink(sinks, logger);
});
string? auditConnectionString = builder.Configuration[PlannerConventions.ConfigurationKeys.AuditConnectionString];
if (!string.IsNullOrWhiteSpace(auditConnectionString))
{
    var auditDataSource = NpgsqlDataSource.Create(auditConnectionString);
    string migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");
    await PostgresAuditOutboxMigrationRunner.ApplyAsync(
        auditDataSource, AuditOutboxConventions.Streams.Planner, migrationsDir, CancellationToken.None).ConfigureAwait(false);
    builder.Services.AddPlannerAuditOutbox(auditDataSource);
}

builder.Services.AddSingleton<BatchProcessor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BatchProcessor>());

var jwtAuthority = builder.Configuration[PlannerConventions.EnvironmentVariables.OAuthAuthority] ?? string.Empty;
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

await ConnectPlannerMcpClientAsync(app).ConfigureAwait(false);

app.UseAuthentication();
app.UseAuthorization();

app.MapPlannerHealthEndpoint();
app.MapPlannerHandoffEndpoint();
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
    string? configuredUrls = builder.Configuration[PlannerConventions.EnvironmentVariables.AspNetCoreUrls];
    if (string.IsNullOrWhiteSpace(configuredUrls))
    {
        builder.WebHost.UseUrls(PlannerConventions.DefaultUrl);
    }
}

static async Task ConnectPlannerMcpClientAsync(WebApplication app)
{
    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("InfraGate.Planner.Startup");
    var mcpClient = app.Services.GetRequiredService<IPlannerMcpClient>();
    var configuration = app.Services.GetRequiredService<IConfiguration>();

    try
    {
        await mcpClient.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

        var allowedNsResponse = await mcpClient.CallToolAsync(
            PlannerConventions.ToolNames.GetAllowedNamespaces,
            null,
            CancellationToken.None).ConfigureAwait(false);

        PlannerLogEvents.LogStartupConnected(
            logger,
            mcpClient.GatewayBaseUrl,
            allowedNsResponse);
    }
    catch (Exception ex)
    {
        var authority = configuration[PlannerConventions.EnvironmentVariables.OAuthAuthority] ?? "(not set)";
        var scope = configuration[PlannerConventions.EnvironmentVariables.OAuthScope] ?? PlannerConventions.DefaultOAuthScope;
        var clientId = configuration[PlannerConventions.EnvironmentVariables.ClientId] ?? PlannerConventions.DefaultClientId;

        PlannerLogEvents.LogStartupConnectionFailed(
            logger,
            mcpClient.GatewayBaseUrl,
            authority,
            scope,
            clientId,
            ex);
    }
}
