namespace InfraGate.RfcRag.Infrastructure;

using Dapper;

internal sealed class RfcRagStartupService
{
    private readonly IOptions<RfcRagOptions> options;
    private readonly NpgsqlDataSource dataSource;
    private readonly IIndexerService indexer;
    private readonly ILogger logger;

    public RfcRagStartupService(
        IOptions<RfcRagOptions> options,
        NpgsqlDataSource dataSource,
        IIndexerService indexer,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this.options = options;
        this.dataSource = dataSource;
        this.indexer = indexer;
        this.logger = loggerFactory.CreateLogger("InfraGate.RfcRag");
    }

    /// <summary>
    /// Runs startup orchestration: migrations, dimension validation, --reindex, and indexing.
    /// Returns <c>true</c> if the application should continue to <c>app.RunAsync</c>,
    /// or <c>false</c> if an early exit was requested (e.g., --reindex).
    /// </summary>
    public async Task<bool> RunStartupAsync(string[] args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        var opts = options.Value;

        if (opts.RunMigrationsOnStartup)
        {
            logger.LogInformation("Applying RFC RAG PostgreSQL migrations.");
            await RfcRagMigrationRunner.ApplyAsync(dataSource, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("RFC RAG PostgreSQL migrations applied.");
        }

        await ValidateEmbeddingDimensionsAsync(dataSource, opts.EmbeddingDimensions, logger)
            .ConfigureAwait(false);

        string? reindexArg = args.FirstOrDefault(a =>
            a.StartsWith("--reindex", StringComparison.OrdinalIgnoreCase));
        bool forceReindex = args.Any(a =>
            string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));

        if (reindexArg is not null)
        {
            return await HandleReindexAsync(reindexArg, forceReindex, cancellationToken)
                .ConfigureAwait(false);
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
            int beforeCount = await indexer.GetIndexedCountAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Starting RFC RAG indexing. IndexedRfcsBefore={IndexedRfcsBefore}", beforeCount);
            await indexer.IndexAllAsync(cancellationToken).ConfigureAwait(false);
            int afterCount = await indexer.GetIndexedCountAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("RFC RAG indexing complete. IndexedRfcsAfter={IndexedRfcsAfter}", afterCount);
        }

        return true;
    }

    private async Task<bool> HandleReindexAsync(
        string reindexArg,
        bool forceReindex,
        CancellationToken cancellationToken)
    {
        string? numberStr = reindexArg.Contains('=', StringComparison.Ordinal)
            ? reindexArg.Split('=', 2)[1]
            : null;

        if (numberStr is null || !int.TryParse(numberStr, out int reindexRfcNumber))
        {
            logger.LogError("Usage: --reindex=<rfc-number> [--force]");
            return false;
        }

        logger.LogInformation(
            "Reindexing single RFC {RfcNumber} (Force={Force}).",
            reindexRfcNumber,
            forceReindex);
        await indexer.IndexSingleAsync(reindexRfcNumber, forceReindex, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Reindex complete for RFC {RfcNumber}.", reindexRfcNumber);
        return false;
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
                int? actualDimensions = await connection.QuerySingleOrDefaultAsync<int?>(
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
}
