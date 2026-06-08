using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
AddRfcRagConfiguration(builder.Configuration, args);

builder.Services.Configure<RfcRagOptions>(
    builder.Configuration.GetSection(RfcRagOptions.SectionName));
builder.Services.AddOptions();

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
    ArgumentException.ThrowIfNullOrWhiteSpace(options.PostgresConnectionString);

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.PostgresConnectionString);
    dataSourceBuilder.UseVector();
    return dataSourceBuilder.Build();
});

builder.Services.AddRfcRagServices();
builder.Services.AddSingleton<RfcRagStartupService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithRequestFilters(filters =>
    {
        filters.AddCallToolFilter(next => (request, cancellationToken) =>
        {
            var services = request.Services;
            if (services is null)
            {
                return next(request, cancellationToken);
            }

            var logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("InfraGate.RfcRag.ToolExceptionFilter");
            return ToolExceptionFilter.CreateSafetyNet(next, logger)(request, cancellationToken);
        });
    })
    .WithListResourcesHandler(static (_, _) =>
        new ValueTask<ListResourcesResult>(new ListResourcesResult { Resources = [] }))
    .WithListPromptsHandler(static (_, _) =>
        new ValueTask<ListPromptsResult>(new ListPromptsResult { Prompts = [] }));

var app = builder.Build();

var startupService = app.Services.GetRequiredService<RfcRagStartupService>();
if (await startupService.RunStartupAsync(args, CancellationToken.None).ConfigureAwait(false))
{
    await app.RunAsync().ConfigureAwait(false);
}

static void AddRfcRagConfiguration(IConfigurationBuilder configuration, string[] args)
{
    configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
    configuration.AddEnvironmentVariables();
    configuration.AddCommandLine(args);
}
