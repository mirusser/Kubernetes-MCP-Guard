using InfraGate.RfcRag.Indexing;
using InfraGate.RfcRag.Infrastructure;
using InfraGate.RfcRag.Models;
using InfraGate.RfcRag.Parsing;
using InfraGate.RfcRag.Search;
using InfraGate.RfcRag.Settings;
using InfraGate.RfcRag.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.RfcRag.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class EmbeddingIntegrationTests : IAsyncLifetime
{
    private const string PostgresImage = "pgvector/pgvector:pg17";

    private PostgreSqlContainer? container;

    public async ValueTask InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage).Build();
        await container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }

    /// <summary>
    /// Verifies that a float[] embedding survives the C# → pgvector → cosine-distance
    /// round-trip without precision loss. Querying with the exact stored vector must
    /// return cosine distance = 0, giving score = 1/(1+0) = 1.0.
    /// </summary>
    [Fact]
    public async Task VectorStorage_KnownVector_SearchVectorScore_EqualsOne()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var indexingRepo = new IndexingRepository(dataSource);
        var searchRepo = new SearchRepository(dataSource);

        // Unit-sphere vector: norm = sqrt(0.6² + 0.8²) = 1.0
        float[] knownVector = new float[1536];
        knownVector[0] = 0.6f;
        knownVector[1] = 0.8f;

        var section = new RfcSection
        {
            Id = Guid.NewGuid(),
            RfcNumber = 1,
            Title = "Round-trip test",
            Section = "1",
            Text = "round-trip test section",
            SourcePath = "rfc1.txt",
            Url = "https://www.rfc-editor.org/rfc/rfc1",
            SourceSha256 = "000",
            Embedding = knownVector
        };

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);
        await indexingRepo.InsertSectionsAsync(connection, transaction, [section], CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        // Querying with the exact stored vector: cosine distance of identical vectors = 0
        // → score = 1/(1+0) = 1.0
        IReadOnlyList<SearchResult> results = await searchRepo.SearchVectorAsync(knownVector, 1, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(1, results[0].RfcNumber);
        Assert.Equal(1.0, results[0].Score, precision: 5);
    }

    /// <summary>
    /// Verifies that SemanticFakeEmbeddingGenerator creates vectors that rank domain-relevant
    /// sections higher: querying "http request response header method" should rank rfc9110
    /// (HTTP Semantics) above rfc3986 (URI Syntax) in pure vector similarity.
    /// </summary>
    [Fact]
    public async Task VectorSearch_SemanticFake_HttpQueryRanksRfc9110_AboveRfc3986()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var searchRepo = new SearchRepository(dataSource);
        var embeddingService = CreateSemanticEmbeddingService();
        var indexer = CreateIndexer(dataSource, embeddingService);

        await indexer.IndexSingleAsync(9110, force: true, CancellationToken.None);
        await indexer.IndexSingleAsync(3986, force: true, CancellationToken.None);

        float[] queryVector = (await embeddingService.GenerateEmbeddingsAsync(
            ["http request response header method"],
            CancellationToken.None))[0];

        IReadOnlyList<SearchResult> results = await searchRepo.SearchVectorAsync(queryVector, 20, CancellationToken.None);
        var resultList = results.ToList();

        int rfc9110FirstRank = resultList.FindIndex(r => r.RfcNumber == 9110);
        int rfc3986FirstRank = resultList.FindIndex(r => r.RfcNumber == 3986);

        Assert.True(rfc9110FirstRank >= 0, "rfc9110 not found in top-20 vector search results");
        Assert.True(
            rfc3986FirstRank == -1 || rfc9110FirstRank < rfc3986FirstRank,
            $"Expected rfc9110 (rank {rfc9110FirstRank}) to rank above rfc3986 (rank {rfc3986FirstRank})");
    }

    /// <summary>
    /// Verifies that TLS-domain queries rank rfc8446 (TLS 1.3) above rfc9110 (HTTP Semantics).
    /// </summary>
    [Fact]
    public async Task VectorSearch_SemanticFake_TlsQueryRanksRfc8446_AboveRfc9110()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var searchRepo = new SearchRepository(dataSource);
        var embeddingService = CreateSemanticEmbeddingService();
        var indexer = CreateIndexer(dataSource, embeddingService);

        await indexer.IndexSingleAsync(8446, force: true, CancellationToken.None);
        await indexer.IndexSingleAsync(9110, force: true, CancellationToken.None);

        float[] queryVector = (await embeddingService.GenerateEmbeddingsAsync(
            ["tls certificate handshake cipher"],
            CancellationToken.None))[0];

        IReadOnlyList<SearchResult> results = await searchRepo.SearchVectorAsync(queryVector, 20, CancellationToken.None);
        var resultList = results.ToList();

        int rfc8446FirstRank = resultList.FindIndex(r => r.RfcNumber == 8446);
        int rfc9110FirstRank = resultList.FindIndex(r => r.RfcNumber == 9110);

        Assert.True(rfc8446FirstRank >= 0, "rfc8446 not found in top-20 vector search results");
        Assert.True(
            rfc9110FirstRank == -1 || rfc8446FirstRank < rfc9110FirstRank,
            $"Expected rfc8446 (rank {rfc8446FirstRank}) to rank above rfc9110 (rank {rfc9110FirstRank})");
    }

    private async Task<NpgsqlDataSource> CreateMigratedDataSourceAsync()
    {
        var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await RfcRagMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        return dataSource;
    }

    private RfcIndexer CreateIndexer(NpgsqlDataSource dataSource, EmbeddingService embeddingService)
    {
        var options = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData"),
            PostgresConnectionString = container!.GetConnectionString(),
            EmbeddingBatchSize = 20
        });

        return new RfcIndexer(
            dataSource,
            new IndexingRepository(dataSource),
            new RfcParser(),
            embeddingService,
            options,
            NullLogger<RfcIndexer>.Instance);
    }

    private static EmbeddingService CreateSemanticEmbeddingService() =>
        new(new SemanticFakeEmbeddingGenerator(), batchSize: 20, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);
}
