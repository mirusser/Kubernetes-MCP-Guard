using A2A;
using InfraGate.Executor;
using InfraGate.Executor.Diagnostics;
using InfraGate.Executor.Endpoints;
using InfraGate.Executor.Handoff;
using InfraGate.Executor.Mcp;
using InfraGate.Executor.Watch;
using InfraGate.Observability;
using InfraGate.RuntimeSafety;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInfraGateEnvironmentVariables(mappings =>
{
    mappings.Map(ExecutorConventions.EnvironmentVariables.GatewayBaseUrl, ExecutorConventions.ConfigurationKeys.GatewayBaseUrl);
    mappings.Map(ExecutorConventions.EnvironmentVariables.ConcurrencyCap, ExecutorConventions.ConfigurationKeys.ConcurrencyCap);
    mappings.Map(ExecutorConventions.EnvironmentVariables.WatchTimeoutSeconds, ExecutorConventions.ConfigurationKeys.WatchTimeoutSeconds);
    RuntimeSafetyConventions.RegisterInfraGateEnvVarMappings(mappings);
});

builder.Services.Configure<ExecutorOptions>(
    builder.Configuration.GetSection(ExecutorConventions.ConfigurationKeys.Executor));

builder.AddInfraGateObservability(opt =>
{
    opt.WriteToConsole = true;
    opt.ConsoleToStandardError = false;
});

ConfigureUrls(builder);

var executorOptions = builder.Configuration
    .GetSection(ExecutorConventions.ConfigurationKeys.Executor)
    .Get<ExecutorOptions>() ?? new ExecutorOptions();

try
{
    executorOptions.Validate();
}
catch (InvalidOperationException ex)
{
    await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
    return 1;
}

var authOptions = new ClientCredentialsTokenOptions
{
    Authority = builder.Configuration[ExecutorConventions.EnvironmentVariables.OAuthAuthority] ?? string.Empty,
    ClientId = builder.Configuration[ExecutorConventions.EnvironmentVariables.ClientId] ?? ExecutorConventions.DefaultClientId,
    ClientSecret = builder.Configuration[ExecutorConventions.EnvironmentVariables.ClientSecret],
    Scope = builder.Configuration[ExecutorConventions.EnvironmentVariables.OAuthScope] ?? ExecutorConventions.DefaultOAuthScope,
    RequireHttpsMetadata = false,
};
builder.Services.AddClientCredentialsTokenProvider(authOptions);

builder.Services.AddSingleton<IExecutorMcpClient, ExecutorMcpClient>();
builder.Services.AddSingleton<IExecutorDedupeStore, ExecutorDedupeStore>();
builder.Services.AddSingleton<ExecutorConcurrencyGate>();
builder.Services.AddSingleton<PlanWatcher>();
builder.Services.AddSingleton<ExecutorAgentHandler>();
#pragma warning disable MEAI001 // Experimental A2A preview package - accepted per plan
builder.Services.AddKeyedSingleton<A2AServer>(ExecutorConventions.A2AHandoffAgentName, (sp, _) =>
{
    var handler = sp.GetRequiredService<ExecutorAgentHandler>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new A2AServer(
        handler,
        new InMemoryTaskStore(),
        new ChannelEventNotifier(),
        loggerFactory.CreateLogger<A2AServer>());
});
#pragma warning restore MEAI001

var jwtAuthority = builder.Configuration[ExecutorConventions.EnvironmentVariables.OAuthAuthority] ?? string.Empty;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwtAuthority;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = false;
        // CA5404: suppressed — Executor is not a registered resource server; the Planner's tokens
        // carry the gateway as their audience. Security is enforced by the PlannerSender policy
        // which checks azp == infra-gate-planner on every inbound request.
#pragma warning disable CA5404
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateAudience = false,
        };
#pragma warning restore CA5404
    });

builder.Services
    .AddAuthorizationBuilder()
    .AddPolicy(ExecutorConventions.Policies.PlannerSender, policy =>
        policy
            .RequireAuthenticatedUser()
            .RequireClaim(ExecutorConventions.Claims.AuthorizedParty, ExecutorConventions.ServiceClients.Planner));

var app = builder.Build();

await ConnectExecutorMcpClientAsync(app).ConfigureAwait(false);

app.UseAuthentication();
app.UseAuthorization();

app.MapExecutorHealthEndpoint();
#pragma warning disable MEAI001 // Experimental A2A preview package - accepted per plan
app.MapA2AHttpJson(ExecutorConventions.A2AHandoffAgentName, ExecutorConventions.A2AHandoffEndpointPath)
   .RequireAuthorization(ExecutorConventions.Policies.PlannerSender);
#pragma warning restore MEAI001
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        context.Response.Redirect(ExecutorConventions.HealthEndpointPath);
        return;
    }
    await next(context).ConfigureAwait(false);
});

await app.RunAsync().ConfigureAwait(false);

return 0;

static void ConfigureUrls(WebApplicationBuilder builder)
{
    string? configuredUrls = builder.Configuration[ExecutorConventions.EnvironmentVariables.AspNetCoreUrls];
    if (string.IsNullOrWhiteSpace(configuredUrls))
    {
        builder.WebHost.UseUrls(ExecutorConventions.DefaultUrl);
    }
}

static async Task ConnectExecutorMcpClientAsync(WebApplication app)
{
    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("InfraGate.Executor.Startup");
    var mcpClient = app.Services.GetRequiredService<IExecutorMcpClient>();
    var configuration = app.Services.GetRequiredService<IConfiguration>();

    try
    {
        await mcpClient.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        ExecutorLogEvents.LogStartupConnected(logger, mcpClient.GatewayBaseUrl);
    }
    catch (Exception ex)
    {
        var authority = configuration[ExecutorConventions.EnvironmentVariables.OAuthAuthority] ?? "(not set)";
        var scope = configuration[ExecutorConventions.EnvironmentVariables.OAuthScope] ?? ExecutorConventions.DefaultOAuthScope;
        var clientId = configuration[ExecutorConventions.EnvironmentVariables.ClientId] ?? ExecutorConventions.DefaultClientId;

        ExecutorLogEvents.LogStartupConnectionFailed(
            logger,
            mcpClient.GatewayBaseUrl,
            authority,
            scope,
            clientId,
            ex);
    }
}
