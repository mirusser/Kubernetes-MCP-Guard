using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace InfraGate.RfcRag;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        AddRfcRagConfiguration(builder.Configuration, args);

        builder.Services.Configure<RfcRagOptions>(
            builder.Configuration.GetSection(RfcRagOptions.SectionName));
        builder.Services.AddOptions();

        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
            ArgumentException.ThrowIfNullOrWhiteSpace(options.PostgresConnectionString);

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.PostgresConnectionString);
            return dataSourceBuilder.Build();
        });

        builder.Services.AddRfcRagServices();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("InfraGate.RfcRag");
        var options = app.Services.GetRequiredService<IOptions<RfcRagOptions>>().Value;
        var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();

        if (options.RunMigrationsOnStartup)
        {
            logger.LogInformation("Applying RFC RAG PostgreSQL migrations.");
            await RfcRagMigrationRunner.ApplyAsync(dataSource, CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation("RFC RAG PostgreSQL migrations applied.");
        }

        var indexer = app.Services.GetRequiredService<IIndexerService>();
        int beforeCount = await indexer.GetIndexedCountAsync(CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Starting RFC RAG indexing. IndexedRfcsBefore={IndexedRfcsBefore}", beforeCount);
        await indexer.IndexAllAsync(CancellationToken.None).ConfigureAwait(false);
        int afterCount = await indexer.GetIndexedCountAsync(CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("RFC RAG indexing complete. IndexedRfcsAfter={IndexedRfcsAfter}", afterCount);

        await app.RunAsync().ConfigureAwait(false);
    }

    private static void AddRfcRagConfiguration(IConfigurationBuilder configuration, string[] args)
    {
        configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(args);
    }
}
