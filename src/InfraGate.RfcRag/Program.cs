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
            dataSourceBuilder.UseVector();
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

        await ValidateEmbeddingDimensionsAsync(dataSource, options.EmbeddingDimensions, logger).ConfigureAwait(false);

        var indexer = app.Services.GetRequiredService<IIndexerService>();

        string? reindexArg = args.FirstOrDefault(a =>
            a.StartsWith("--reindex", StringComparison.OrdinalIgnoreCase));
        bool forceReindex = args.Any(a =>
            string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));

        if (reindexArg is not null)
        {
            string? numberStr = reindexArg.Contains('=', StringComparison.Ordinal)
                ? reindexArg.Split('=', 2)[1]
                : null;

            if (numberStr is null || !int.TryParse(numberStr, out int reindexRfcNumber))
            {
                logger.LogError("Usage: --reindex=<rfc-number> [--force]");
                return;
            }

            logger.LogInformation(
                "Reindexing single RFC {RfcNumber} (Force={Force}).",
                reindexRfcNumber,
                forceReindex);
            await indexer.IndexSingleAsync(reindexRfcNumber, forceReindex, CancellationToken.None)
                .ConfigureAwait(false);
            logger.LogInformation("Reindex complete for RFC {RfcNumber}.", reindexRfcNumber);
            return;
        }

        string? openRouterKey = Environment.GetEnvironmentVariable(
            RfcRagOptions.OpenRouterApiKeyEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(openRouterKey))
        {
            logger.LogWarning(
                "{EnvVar} is not set. Skipping startup indexing. " +
                "Search queries against already-indexed data will still work. " +
                "Set this environment variable to enable embedding generation.",
                RfcRagOptions.OpenRouterApiKeyEnvironmentVariable);
        }
        else
        {
            int beforeCount = await indexer.GetIndexedCountAsync(CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation("Starting RFC RAG indexing. IndexedRfcsBefore={IndexedRfcsBefore}", beforeCount);
            await indexer.IndexAllAsync(CancellationToken.None).ConfigureAwait(false);
            int afterCount = await indexer.GetIndexedCountAsync(CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation("RFC RAG indexing complete. IndexedRfcsAfter={IndexedRfcsAfter}", afterCount);
        }

        await app.RunAsync().ConfigureAwait(false);
    }

    private static async Task ValidateEmbeddingDimensionsAsync(
        NpgsqlDataSource dataSource,
        int expectedDimensions,
        ILogger logger)
    {
        try
        {
            var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                int? actualDimensions = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<int?>(connection,
                """
                select character_maximum_length
                from information_schema.columns
                where table_schema = 'rfc_rag'
                  and table_name = 'rfc_sections'
                  and column_name = 'embedding'
                """).ConfigureAwait(false);

            if (actualDimensions is not null && actualDimensions.Value != expectedDimensions)
            {
                throw new InvalidOperationException(
                    $"RFC RAG embedding dimension mismatch: the 'rfc_sections.embedding' column expects " +
                    $"{actualDimensions} dimensions, but RfcRagOptions.EmbeddingDimensions is configured to " +
                    $"{expectedDimensions}. Update EmbeddingDimensions in your configuration to match, or " +
                    $"change the embedding model to produce {actualDimensions}-dimensional vectors.");
            }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not validate RFC RAG embedding dimensions. " +
                "Skipping dimension check. Expected={ExpectedDimensions}",
                expectedDimensions);
        }
    }

    private static void AddRfcRagConfiguration(IConfigurationBuilder configuration, string[] args)
    {
        configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(args);
    }
}
