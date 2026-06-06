using Dapper;
using InfraGate.RfcRag.Indexing;
using InfraGate.RfcRag.Infrastructure;
using InfraGate.RfcRag.Models;
using InfraGate.RfcRag.Parsing;
using InfraGate.RfcRag.Search;
using InfraGate.RfcRag.Settings;
using InfraGate.RfcRag.Tests.Fakes;
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
        var result = results[0];
        Assert.Equal(9110, result.RfcNumber);
        Assert.Contains("HTTP Semantics", result.Title);
        Assert.NotNull(result.Section);
    }

    [Fact]
    public async Task GetSectionAsync_WithExistingSection_ReturnsExactMatch()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        ISearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        RfcSection? section = await search.GetSectionAsync(2119, "1", CancellationToken.None);

        Assert.NotNull(section);
        Assert.Contains("MUST", section.Text);
        Assert.Equal("1", section.Section);
        Assert.Equal(2119, section.RfcNumber);
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
        Assert.Contains(results, result => result.Excerpt.Contains("MUST"));
    }

    [Fact]
    public async Task SearchAbnf_FindsGrammarBlocks_AcrossRecentRfcs()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        SearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        IReadOnlyList<SearchResult> uriResults = await search.SearchAbnfAsync(
            "URI", null, 10, CancellationToken.None);
        Assert.Contains(uriResults, r => r.RfcNumber == 3986);

        IReadOnlyList<SearchResult> httpResults = await search.SearchAbnfAsync(
            "request-line", null, 10, CancellationToken.None);
        Assert.Contains(httpResults, r => r.RfcNumber == 9110);

        IReadOnlyList<SearchResult> quicResults = await search.SearchAbnfAsync(
            "transport_parameters", null, 10, CancellationToken.None);
        Assert.Contains(quicResults, r => r.RfcNumber == 9000);

        IReadOnlyList<SearchResult> filteredResults = await search.SearchAbnfAsync(
            "URI", [3986], 10, CancellationToken.None);
        Assert.Contains(filteredResults, r => r.RfcNumber == 3986);
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

        Assert.True(originalCount > 0);
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
        var indexingRepository = new IndexingRepository(dataSource);
        var embeddingService = CreateEmbeddingService();
        var options = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData"),
            PostgresConnectionString = container!.GetConnectionString(),
            EmbeddingBatchSize = 5
        });

        return new RfcIndexer(
            dataSource,
            indexingRepository,
            new RfcParser(),
            embeddingService,
            options,
            NullLogger<RfcIndexer>.Instance);
    }

    private static SearchService CreateSearchService(NpgsqlDataSource dataSource) =>
        new(new SearchRepository(dataSource), new MetadataRepository(dataSource), CreateEmbeddingService());

    private static EmbeddingService CreateEmbeddingService() =>
        new(new FakeEmbeddingGenerator(), 5, NullLogger<EmbeddingService>.Instance);
}
