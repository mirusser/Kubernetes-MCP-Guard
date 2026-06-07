using InfraGate.RfcRag.Indexing;
using InfraGate.RfcRag.Infrastructure;
using InfraGate.RfcRag.Parsing;
using InfraGate.RfcRag.Search;
using InfraGate.RfcRag.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfraGate.RfcRag.Tests.IntegrationTests;

/// <summary>
/// Indexes ~90 well-known RFCs using the real OpenRouter embedding API and verifies
/// that semantic search returns domain-relevant results. Skipped when
/// <c>InfraGate__OpenRouter__ApiKey</c> is not set.
///
/// Run: dotnet test --filter "Category=LiveApi"
/// </summary>
public sealed class LiveApiFixture : IAsyncLifetime
{
    private const string PostgresImage = "pgvector/pgvector:pg17";

    // ~90 RFCs spanning distinct technical domains. Each domain cluster is large enough
    // for vector search to reliably rank its representative RFC in the top results.
    internal static readonly int[] RfcSubset =
    [
        // Foundational / normative language
        791, 793, 2119, 8174,
        // DNS / DNSSEC
        1034, 1035, 2782, 4034, 4035, 6891,
        // SNMP
        1157, 3418,
        // DHCP
        2131, 3315,
        // MIME
        2045, 2046, 2047, 2048, 2049,
        // OSPF / BGP / routing
        2328, 4271, 4760,
        // IPv6
        2460, 4291, 8200,
        // RADIUS
        2865,
        // SIP
        3261, 3263,
        // URI
        3875, 3986,
        // SSH
        4251, 4252, 4253, 4254,
        // PKI / TLS / certificates
        4210, 5246, 5280, 5652, 6066, 6125, 7301, 8032, 8446,
        // SMTP / email
        4954, 5321, 5322, 6152,
        // NTP / time
        5905, 8915,
        // Kerberos
        4120,
        // LDAP
        4511, 4512, 4513,
        // XMPP
        6120, 6121,
        // OAuth 2.0 / JWT / OIDC
        6749, 6750, 7517, 7518, 7519, 7523, 8414, 8693,
        // HTTP cookies
        6265,
        // WebSocket
        6455,
        // JSON
        6901, 6902, 7807, 8259,
        // HTTP/1.1 (legacy)
        7230, 7231, 7232, 7233, 7234, 7235,
        // CoAP
        7252,
        // HTTP/2 + HPACK
        7540, 7541,
        // STUN / ICE
        5389, 8445, 8839,
        // QUIC
        9000, 9001, 9002, 9221,
        // HTTP semantics (RFC 9110 family)
        9110, 9111, 9112, 9113, 9114,
        // FTP / Telnet
        854, 959,
    ];

    private PostgreSqlContainer? container;

    public string? ApiKey { get; } =
        Environment.GetEnvironmentVariable(RfcRagOptions.OpenRouterApiKeyEnvironmentVariable);

    public bool HasApiKey => !string.IsNullOrEmpty(ApiKey);

    public NpgsqlDataSource? DataSource { get; private set; }
    public EmbeddingService? EmbeddingService { get; private set; }
    public int IndexedCount { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!HasApiKey) return;

        container = new PostgreSqlBuilder(PostgresImage).Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        DataSource = NpgsqlDataSource.Create(container.GetConnectionString());
        await RfcRagMigrationRunner.ApplyAsync(DataSource, CancellationToken.None);

        var rfcOptions = new RfcRagOptions
        {
            RfcMirrorPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData"),
            PostgresConnectionString = container.GetConnectionString(),
            EmbeddingBatchSize = 20,
            MaxEmbeddingConcurrency = 4,
        };

        EmbeddingService = new EmbeddingService(
            OpenAiEmbeddingGeneratorAdapter.Create(ApiKey!, rfcOptions.OpenRouterEmbeddingEndpoint, rfcOptions.EmbeddingModel),
            rfcOptions.EmbeddingBatchSize,
            rfcOptions.MaxEmbeddingConcurrency,
            NullLogger<EmbeddingService>.Instance);

        var indexer = new RfcIndexer(
            DataSource,
            new IndexingRepository(DataSource),
            new RfcParser(),
            EmbeddingService,
            Options.Create(rfcOptions),
            NullLogger<RfcIndexer>.Instance);

        foreach (int rfcNumber in RfcSubset)
            await indexer.IndexSingleAsync(rfcNumber, force: true, CancellationToken.None);

        IndexedCount = await indexer.GetIndexedCountAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (DataSource is not null) await DataSource.DisposeAsync();
        if (container is not null) await container.DisposeAsync();
    }
}

[Trait("Category", "LiveApi")]
public sealed class LiveApiIndexingTests(LiveApiFixture fixture) : IClassFixture<LiveApiFixture>
{
    private static readonly string SkipReason =
        $"Set {RfcRagOptions.OpenRouterApiKeyEnvironmentVariable} to run live API tests";

    [Fact]
    public void IndexSubset_AllRfcsIndexed_CountMatchesSubset()
    {
        if (!fixture.HasApiKey) Assert.Skip(SkipReason);

        Assert.Equal(LiveApiFixture.RfcSubset.Length, fixture.IndexedCount);
    }

    [Fact]
    public async Task VectorSearch_HttpQuery_RanksRfc9110_InTop5()
    {
        if (!fixture.HasApiKey) Assert.Skip(SkipReason);

        var searchRepo = new SearchRepository(fixture.DataSource!);
        float[] queryVector = (await fixture.EmbeddingService!.GenerateEmbeddingsAsync(
            ["HTTP semantics request response methods headers status codes"],
            CancellationToken.None))[0];

        IReadOnlyList<SearchResult> results = await searchRepo.SearchVectorAsync(queryVector, 20, CancellationToken.None);
        int rank = results.ToList().FindIndex(r => r.RfcNumber == 9110);

        Assert.True(rank >= 0, "rfc9110 not found in top-20 results");
        Assert.True(rank < 5, $"Expected rfc9110 in top-5, got rank {rank}");
    }

    [Fact]
    public async Task VectorSearch_TlsQuery_RanksRfc8446_InTop5()
    {
        if (!fixture.HasApiKey) Assert.Skip(SkipReason);

        var searchRepo = new SearchRepository(fixture.DataSource!);
        float[] queryVector = (await fixture.EmbeddingService!.GenerateEmbeddingsAsync(
            ["TLS 1.3 handshake certificate cipher suite record layer"],
            CancellationToken.None))[0];

        IReadOnlyList<SearchResult> results = await searchRepo.SearchVectorAsync(queryVector, 20, CancellationToken.None);
        int rank = results.ToList().FindIndex(r => r.RfcNumber == 8446);

        Assert.True(rank >= 0, "rfc8446 not found in top-20 results");
        Assert.True(rank < 5, $"Expected rfc8446 in top-5, got rank {rank}");
    }

    [Fact]
    public async Task VectorSearch_JwtOauthQuery_RanksRfc7519OrRfc6749_InTop10()
    {
        if (!fixture.HasApiKey) Assert.Skip(SkipReason);

        var searchRepo = new SearchRepository(fixture.DataSource!);
        float[] queryVector = (await fixture.EmbeddingService!.GenerateEmbeddingsAsync(
            ["JWT JSON web token claims authorization bearer OAuth access token"],
            CancellationToken.None))[0];

        IReadOnlyList<SearchResult> results = await searchRepo.SearchVectorAsync(queryVector, 20, CancellationToken.None);
        var resultList = results.ToList();

        int jwt = resultList.FindIndex(r => r.RfcNumber == 7519);
        int oauth = resultList.FindIndex(r => r.RfcNumber == 6749);

        bool eitherInTop10 = (jwt >= 0 && jwt < 10) || (oauth >= 0 && oauth < 10);
        Assert.True(eitherInTop10, $"Expected rfc7519 (rank {jwt}) or rfc6749 (rank {oauth}) in top-10");
    }
}
