using A2A;
using InfraGate.Executor;
using InfraGate.Executor.Diagnostics;
using InfraGate.Executor.Endpoints;
using InfraGate.Executor.Handoff;
using InfraGate.Executor.Mcp;
using InfraGate.Executor.Watch;
using InfraGate.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// Executor configuration binds from the single InfraGate:Executor section (appsettings.json +
// InfraGate__Executor__* environment overrides). No manual env-var mapping or per-key reads.
builder.Services.Configure<ExecutorOptions>(
    builder.Configuration.GetSection(ExecutorConventions.SectionName));

builder.AddInfraGateObservability(opt =>
{
    opt.WriteToConsole = true;
    opt.ConsoleToStandardError = false;
});

ConfigureUrls(builder);

var executorOptions = builder.Configuration
    .GetSection(ExecutorConventions.SectionName)
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
    Authority = executorOptions.OAuthAuthority,
    ClientId = executorOptions.ClientId,
    ClientSecret = executorOptions.ClientSecret,
    Scope = executorOptions.OAuthScope,
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

var jwtAuthority = executorOptions.OAuthAuthority;
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

await ConnectExecutorMcpClientAsync(app, executorOptions).ConfigureAwait(false);

app.UseAuthentication();
app.UseAuthorization();

app.MapExecutorHealthEndpoint();
#pragma warning disable MEAI001 // Experimental A2A preview package - accepted per plan
app.MapA2AJsonRpc(ExecutorConventions.A2AHandoffAgentName, ExecutorConventions.A2AHandoffEndpointPath)
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
    string? configuredUrls = builder.Configuration[ExecutorConventions.AspNetCoreUrlsKey];
    if (string.IsNullOrWhiteSpace(configuredUrls))
    {
        builder.WebHost.UseUrls(ExecutorConventions.DefaultUrl);
    }
}

static async Task ConnectExecutorMcpClientAsync(WebApplication app, ExecutorOptions options)
{
    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("InfraGate.Executor.Startup");
    var mcpClient = app.Services.GetRequiredService<IExecutorMcpClient>();

    try
    {
        await mcpClient.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        ExecutorLogEvents.LogStartupConnected(logger, mcpClient.GatewayBaseUrl);
    }
    catch (Exception ex)
    {
        var authority = string.IsNullOrEmpty(options.OAuthAuthority) ? "(not set)" : options.OAuthAuthority;
        var scope = options.OAuthScope;
        var clientId = options.ClientId;

        ExecutorLogEvents.LogStartupConnectionFailed(
            logger,
            mcpClient.GatewayBaseUrl,
            authority,
            scope,
            clientId,
            ex);
    }
}
