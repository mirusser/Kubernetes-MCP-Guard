using Dapper;
using InfraGate.RfcRag.Models;
using InfraGate.RfcRag.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.RfcRag.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class RfcRagIntegrationTests : IAsyncLifetime
{
    private const string PostgresImage = "pgvector/pgvector:pg17";
    private const int EmbeddingDimensions = 1536;

    private static readonly string[] ExpectedTables =
    [
        "indexed_rfcs",
        "normative_occurrences",
        "rfc_abnf_blocks",
        "rfc_sections",
        "schema_migrations"
    ];

    private PostgreSqlContainer? container;

    public async Task InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage)
            .Build();

        await container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task RunMigrations_OnEmptyDatabase_CreatesExpectedTables()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await RfcRagMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        var actual = (await connection.QueryAsync<string>(
                """
                select table_name
                from information_schema.tables
                where table_schema = 'rfc_rag'
                order by table_name
                """))
            .ToArray();

        Assert.Equal(ExpectedTables, actual);
    }

    [Fact]
    public async Task IndexAndSearch_WithFixtureRfcs_ReturnsRelevantResults()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        ISearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        int indexedCount = await indexer.GetIndexedCountAsync(CancellationToken.None);
        IReadOnlyList<SearchResult> results = await search.SearchAsync("HTTP semantics", 10, CancellationToken.None);

        Assert.True(indexedCount > 0 && results.Count > 0);
    }

    [Fact]
    public async Task GetSectionAsync_WithExistingSection_ReturnsExactMatch()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        ISearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        RfcSection? section = await search.GetSectionAsync(2119, "1", CancellationToken.None);

        Assert.Contains("MUST", section?.Text);
    }

    [Fact]
    public async Task SearchNormativeAsync_WithKeyword_ReturnsMatchingSections()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        ISearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        IReadOnlyList<SearchResult> results = await search.SearchNormativeAsync("MUST", null, 20, CancellationToken.None);

        Assert.Contains(results, result => result.RfcNumber == 2119);
    }

    [Fact]
    public async Task IncrementalIndex_SkipsUnchangedRfcs()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);
        int originalCount = await indexer.GetIndexedCountAsync(CancellationToken.None);

        await indexer.IndexAllAsync(CancellationToken.None);
        int incrementalCount = await indexer.GetIndexedCountAsync(CancellationToken.None);

        Assert.Equal(originalCount, incrementalCount);
    }

    private async Task<NpgsqlDataSource> CreateMigratedDataSourceAsync()
    {
        var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await RfcRagMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        return dataSource;
    }

    private RfcIndexer CreateIndexer(NpgsqlDataSource dataSource)
    {
        var repository = new RfcRepository();
        var embeddingService = CreateEmbeddingService();
        var options = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData"),
            PostgresConnectionString = container!.GetConnectionString(),
            EmbeddingBatchSize = 5
        });

        return new RfcIndexer(
            dataSource,
            repository,
            new RfcParser(),
            embeddingService,
            options,
            NullLogger<RfcIndexer>.Instance);
    }

    private static SearchService CreateSearchService(NpgsqlDataSource dataSource) =>
        new(dataSource, new RfcRepository(), CreateEmbeddingService());

    private static EmbeddingService CreateEmbeddingService() =>
        new(new FakeEmbeddingGenerator(), 5, NullLogger<EmbeddingService>.Instance);

    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        // Required by the embedding generator shape even though the fake metadata is constant.
#pragma warning disable MA0041
        public EmbeddingGeneratorMetadata Metadata => new("fake", new Uri("http://localhost"));
#pragma warning restore MA0041

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = values.ToList();
            var results = new List<Embedding<float>>(list.Count);

            for (int i = 0; i < list.Count; i++)
            {
                float[] vector = new float[EmbeddingDimensions];
                int hash = list[i].GetHashCode(StringComparison.Ordinal);
                for (int j = 0; j < EmbeddingDimensions; j++)
                {
                    vector[j] = (float)((hash * (j + 1) * 0.001) % 1.0);
                }

                results.Add(new Embedding<float>(vector));
            }

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
        }

        public object? GetService(Type? serviceType, object? key = null) => null;

        public void Dispose()
        {
        }
    }
}
