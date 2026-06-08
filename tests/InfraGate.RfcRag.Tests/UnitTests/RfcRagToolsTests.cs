using System.Text.Json;
using InfraGate.RfcRag.Indexing;
using InfraGate.RfcRag.Models;
using InfraGate.RfcRag.Search;
using InfraGate.RfcRag.Tests.Fakes;
using InfraGate.RfcRag.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace InfraGate.RfcRag.Tests.UnitTests;

public sealed class RfcRagToolsTests
{
    [Fact]
    public async Task SearchRfc_ReturnsValidJson()
    {
        var fake = new FakeSearchService
        {
            SearchResults = [new SearchResult(
                Guid.NewGuid(), 2119, "Key words", "1", "Introduction",
                "The key words MUST", "rfc2119.txt",
                "https://www.rfc-editor.org/rfc/rfc2119", 0.95)]
        };

        string json = await RfcRagTools.SearchRfc(fake, "test", 10, CancellationToken.None);

        Assert.NotNull(json);
        Assert.StartsWith("[", json);
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2119, root[0].GetProperty("rfcNumber").GetInt32());
    }

    [Fact]
    public async Task GetRfc_WithSections_ReturnsTocAndPreview()
    {
        var fake = new FakeSearchService
        {
            RfcSections = [
                new RfcSection
                {
                    Id = Guid.NewGuid(),
                    RfcNumber = 2119,
                    Title = "Key words",
                    Section = "1",
                    Heading = "Introduction",
                    Text = "The key words MUST",
                    SourcePath = "rfc2119.txt",
                    Url = "https://www.rfc-editor.org/rfc/rfc2119"
                },
                new RfcSection
                {
                    Id = Guid.NewGuid(),
                    RfcNumber = 2119,
                    Title = "Key words",
                    Section = "2",
                    Heading = "Definitions",
                    Text = "Definitions section.",
                    SourcePath = "rfc2119.txt",
                    Url = "https://www.rfc-editor.org/rfc/rfc2119"
                }
            ]
        };

        string json = await RfcRagTools.GetRfc(fake, 2119, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(2119, root.GetProperty("rfcNumber").GetInt32());
        Assert.Equal("Key words", root.GetProperty("title").GetString());
        Assert.Equal(2, root.GetProperty("sectionCount").GetInt32());
        Assert.True(root.TryGetProperty("toc", out JsonElement toc));
        Assert.Equal("Introduction", toc.GetProperty("1").GetString());
        Assert.Equal("Definitions", toc.GetProperty("2").GetString());
        Assert.True(root.TryGetProperty("sections", out JsonElement preview));
        Assert.Equal(2, preview.GetArrayLength());
        Assert.Equal("1", preview[0].GetProperty("section").GetString());
        Assert.False(root.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task GetRfc_NoSections_ReturnsError()
    {
        var fake = new FakeSearchService { RfcSections = [] };

        string json = await RfcRagTools.GetRfc(fake, 9999, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task GetRfcFull_WithSections_ReturnsFullText()
    {
        var fake = new FakeSearchService
        {
            RfcSections = [
                new RfcSection
                {
                    RfcNumber = 2119,
                    Title = "Key words",
                    Section = "1",
                    Heading = "Introduction",
                    Text = "The key words MUST",
                    SourcePath = "rfc2119.txt",
                    Url = "https://www.rfc-editor.org/rfc/rfc2119"
                },
                new RfcSection
                {
                    RfcNumber = 2119,
                    Title = "Key words",
                    Section = "2",
                    Heading = "Definitions",
                    Text = "Definitions of terms.",
                    SourcePath = "rfc2119.txt",
                    Url = "https://www.rfc-editor.org/rfc/rfc2119"
                }
            ]
        };

        string json = await RfcRagTools.GetRfcFull(fake, 2119, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(2119, root.GetProperty("rfcNumber").GetInt32());
        Assert.Equal(2, root.GetProperty("sectionCount").GetInt32());
        Assert.True(root.TryGetProperty("text", out JsonElement text));
        Assert.Contains("The key words MUST", text.GetString());
        Assert.Contains("Definitions of terms.", text.GetString());
        Assert.Contains("\n\n", text.GetString());
    }

    [Fact]
    public async Task GetRfcFull_NoSections_ReturnsError()
    {
        var fake = new FakeSearchService { RfcSections = [] };

        string json = await RfcRagTools.GetRfcFull(fake, 9999, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task GetRfcSection_WithResult_ReturnsSection()
    {
        string json = await RfcRagTools.GetRfcSection(
            new FakeSearchService
            {
                SingleSection = new RfcSection
                {
                    Id = Guid.NewGuid(),
                    RfcNumber = 2119,
                    Title = "Key words for use in RFCs",
                    Section = "1",
                    Heading = "Introduction",
                    Text = "This is a test section.",
                    SourcePath = "rfc2119.txt",
                    Url = "https://www.rfc-editor.org/rfc/rfc2119"
                }
            },
             2119,
            "1",
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(2119, root.GetProperty("rfcNumber").GetInt32());
        Assert.Equal("1", root.GetProperty("section").GetString());
    }

    [Fact]
    public async Task GetRfcSection_NullResult_ReturnsError()
    {
        var fake = new FakeSearchService { SingleSection = null };

        string json = await RfcRagTools.GetRfcSection(fake, 9999, "99", cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task SearchNormative_ReturnsValidJson()
    {
        var fake = new FakeSearchService
        {
            SearchResults = [new SearchResult(
                Guid.NewGuid(), 2119, "Key words", "1", null,
                "The key words MUST", "rfc2119.txt",
                "https://www.rfc-editor.org/rfc/rfc2119", 0.85)]
        };

        string json = await RfcRagTools.SearchNormative(fake, "MUST", null, 10, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.NotEmpty(doc.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task SearchAbnf_ReturnsValidJson()
    {
        var fake = new FakeSearchService
        {
            SearchResults = [new SearchResult(
                Guid.NewGuid(), 9110, "HTTP Semantics", "5.3", "Field Names",
                "field-name = token", "rfc9110.txt",
                "https://www.rfc-editor.org/rfc/rfc9110", 0.75)]
        };

        string json = await RfcRagTools.SearchAbnf(fake, "field-name", null, 10, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task SearchRfc_EmptyDb_ReturnsEmptyArray()
    {
        var fake = new FakeSearchService { SearchResults = [] };

        string json = await RfcRagTools.SearchRfc(fake, "HTTP", 10, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Empty(doc.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task SearchNormative_EmptyDb_ReturnsEmptyArray()
    {
        var fake = new FakeSearchService { SearchResults = [] };

        string json = await RfcRagTools.SearchNormative(fake, "MUST", null, 10, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Empty(doc.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task SearchAbnf_EmptyDb_ReturnsEmptyArray()
    {
        var fake = new FakeSearchService { SearchResults = [] };

        string json = await RfcRagTools.SearchAbnf(fake, "ALPHA", null, 10, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Empty(doc.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task FindUpdatesObsoletes_MissingRfc_ReturnsError()
    {
        var fake = new FakeSearchService { Metadata = null };

        string json = await RfcRagTools.FindUpdatesObsoletes(fake, 9999, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task FindUpdatesObsoletes_WithMetadata_ReturnsRelationships()
    {
        var fake = new FakeSearchService
        {
            Metadata = new RfcMetadata
            {
                Number = 7230,
                Title = "HTTP/1.1 Message Syntax",
                Updates = [7231],
                Obsoletes = [2616]
            },
            BackReferences = [new RfcMetadata { Number = 9110, Title = "HTTP Semantics" }]
        };

        string json = await RfcRagTools.FindUpdatesObsoletes(fake, 7230, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(7230, root.GetProperty("rfcNumber").GetInt32());
        Assert.NotEmpty(root.GetProperty("updates").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("obsoletes").EnumerateArray());
    }

    [Fact]
    public async Task GetRfcMetadata_Found_ReturnsMetadata()
    {
        var fake = new FakeSearchService
        {
            Metadata = new RfcMetadata
            {
                Number = 9110,
                Title = "HTTP Semantics"
            }
        };

        string json = await RfcRagTools.GetRfcMetadata(fake, 9110, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(9110, root.GetProperty("number").GetInt32());
        Assert.Equal("HTTP Semantics", root.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetRfcMetadata_NotFound_ReturnsError()
    {
        var fake = new FakeSearchService { Metadata = null };

        string json = await RfcRagTools.GetRfcMetadata(fake, 9999, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ListIndexedRfcs_ReturnsResults()
    {
        var fake = new FakeSearchService
        {
            IndexedRfcList = [
                new RfcMetadata { Number = 2119, Title = "Key words" },
                new RfcMetadata { Number = 8446, Title = "TLS 1.3" }
            ]
        };

        string json = await RfcRagTools.ListIndexedRfcs(fake, 10, 0, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("rfcs").GetArrayLength());
        Assert.Equal(2119, root.GetProperty("rfcs")[0].GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task ListIndexedRfcs_Empty_ReturnsZero()
    {
        var fake = new FakeSearchService { IndexedRfcList = [] };

        string json = await RfcRagTools.ListIndexedRfcs(fake, 10, 0, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task RfcMetadata_GrammarStyle_DefaultsToNone()
    {
        var metadata = new RfcMetadata();

        Assert.Equal(GrammarStyleConstants.None, metadata.GrammarStyle);
    }

    [Fact]
    public async Task GetRfcMetadata_IncludesGrammarStyle()
    {
        var fake = new FakeSearchService
        {
            Metadata = new RfcMetadata
            {
                Number = 8446,
                Title = "TLS 1.3",
                GrammarStyle = GrammarStyleConstants.TlsPresentationLang
            }
        };

        string json = await RfcRagTools.GetRfcMetadata(fake, 8446, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(GrammarStyleConstants.TlsPresentationLang, doc.RootElement.GetProperty("grammarStyle").GetString());
    }

    [Fact]
    public async Task RfcStats_ReturnsStatsJson()
    {
        var fake = new FakeSearchService
        {
            StatsJson = """{"indexedRfcs":100,"sections":5000,"abnfBlocks":200,"normativeOccurrences":30000,"lastIndexedAtUtc":"2026-06-06T00:00:00Z"}"""
        };

        string json = await RfcRagTools.RfcStats(fake, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(100, doc.RootElement.GetProperty("indexedRfcs").GetInt32());
    }

    [Fact]
    public async Task GetRfcToc_WithSections_ReturnsOrderedMap()
    {
        var fake = new FakeSearchService
        {
            RfcSections = [
                new RfcSection { Section = "4", Heading = "Handshake Protocol" },
                new RfcSection { Section = "4.1", Heading = "Key Exchange Messages" },
                new RfcSection { Section = "4.1.1", Heading = "Cryptographic Negotiation" },
                new RfcSection { Section = "4.1.2", Heading = null }
            ],
            TocMap = new Dictionary<string, string?>
            {
                ["4"] = "Handshake Protocol",
                ["4.1"] = "Key Exchange Messages",
                ["4.1.1"] = "Cryptographic Negotiation",
                ["4.1.2"] = null
            }
        };

        string json = await RfcRagTools.GetRfcToc(fake, 8446, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Handshake Protocol", doc.RootElement.GetProperty("4").GetString());
        Assert.Equal("Key Exchange Messages", doc.RootElement.GetProperty("4.1").GetString());
        Assert.Equal("Cryptographic Negotiation", doc.RootElement.GetProperty("4.1.1").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("4.1.2").ValueKind);
    }

    [Fact]
    public async Task GetRfcToc_NoSections_ReturnsError()
    {
        var fake = new FakeSearchService
        {
            RfcSections = [],
            TocMap = new Dictionary<string, string?>()
        };

        string json = await RfcRagTools.GetRfcToc(fake, 9999, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task GetRfcSection_Depth0_ReturnsSingleSection()
    {
        string json = await RfcRagTools.GetRfcSection(
            new FakeSearchService
            {
                SingleSection = new RfcSection
                {
                    RfcNumber = 8446,
                    Section = "4.4",
                    Heading = "Extensions",
                    Text = "Extensions section text."
                }
            },
             8446,
            "4.4",
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(8446, doc.RootElement.GetProperty("rfcNumber").GetInt32());
        Assert.Equal("4.4", doc.RootElement.GetProperty("section").GetString());
    }

    [Fact]
    public async Task GetRfcSection_Depth1_ReturnsSectionWithChildren()
    {
        var fake = new FakeSearchService
        {
            SectionWithChildren = new FakeSearchService.SectionTree
            {
                Parent = new RfcSection { RfcNumber = 8446, Section = "4.4", Heading = "Extensions" },
                Children = [
                    new RfcSection { RfcNumber = 8446, Section = "4.4.1", Heading = "Signed Certificate Timestamp" },
                    new RfcSection { RfcNumber = 8446, Section = "4.4.2", Heading = "Certificate Authorities" }
                ]
            }
        };

        string json = await RfcRagTools.GetRfcSection(fake, 8446, "4.4", depth: 1, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("4.4", doc.RootElement.GetProperty("section").GetProperty("section").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("children").GetArrayLength());
    }

    [Fact]
    public async Task GetRfcSection_Depth1WithExpand_IgnoresExpandFlag()
    {
        var fake = new FakeSearchService
        {
            SectionWithChildren = new FakeSearchService.SectionTree
            {
                Parent = new RfcSection { RfcNumber = 8446, Section = "4.4", Heading = "Extensions" },
                Children = [new RfcSection { RfcNumber = 8446, Section = "4.4.1", Heading = "Signed Certificate Timestamp" }]
            },
            ExpandedTypes = new Dictionary<string, RfcSection>
            {
                ["SignatureScheme"] = new RfcSection { RfcNumber = 8446, Section = "4.2.3", Heading = "Signature Algorithms" }
            }
        };

        string json = await RfcRagTools.GetRfcSection(fake, 8446, "4.4", depth: 1, expand: true, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.True(root.TryGetProperty("section", out _));
        Assert.True(root.TryGetProperty("children", out _));
        Assert.False(root.TryGetProperty("expandedTypes", out _));
    }

    [Fact]
    public async Task GetRfcSection_Expand_IncludesReferencedTypes()
    {
        var fake = new FakeSearchService
        {
            SingleSection = new RfcSection
            {
                RfcNumber = 8446,
                Section = "4.4.3",
                Heading = "Certificate Extensions",
                Text = "This message uses SignatureScheme and HandshakeType for extensions."
            },
            ExpandedTypes = new Dictionary<string, RfcSection>
            {
                ["SignatureScheme"] = new RfcSection { RfcNumber = 8446, Section = "4.2.3", Heading = "Signature Algorithms", Text = "enum { ... } SignatureScheme;" },
                ["HandshakeType"] = new RfcSection { RfcNumber = 8446, Section = "4.1.1", Heading = "Cryptographic Negotiation", Text = "enum { client_hello(1) ... } HandshakeType;" }
            }
        };

        string json = await RfcRagTools.GetRfcSection(fake, 8446, "4.4.3", expand: true, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal("4.4.3", root.GetProperty("section").GetProperty("section").GetString());
        Assert.True(root.TryGetProperty("expandedTypes", out JsonElement expanded));
        Assert.Equal(2, expanded.EnumerateObject().Count());
        Assert.True(expanded.TryGetProperty("SignatureScheme", out JsonElement sig));
        Assert.Equal("4.2.3", sig.GetProperty("section").GetString());
        Assert.True(expanded.TryGetProperty("HandshakeType", out JsonElement hs));
        Assert.Equal("4.1.1", hs.GetProperty("section").GetString());
    }

    [Fact]
    public async Task GetRfcSection_Expand_NoReferences_ReturnsSectionAlone()
    {
        var fake = new FakeSearchService
        {
            SingleSection = new RfcSection
            {
                RfcNumber = 8446,
                Section = "6.1",
                Heading = "Introduction",
                Text = "This section has no type references to expand."
            },
            ExpandedTypes = new Dictionary<string, RfcSection>()
        };

        string json = await RfcRagTools.GetRfcSection(fake, 8446, "6.1", expand: true, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("expandedTypes", out _));
    }

    [Fact]
    public async Task SearchRfc_WhenSearchThrows_PropagatesException()
    {
        var expected = new InvalidOperationException("DB down");
        var fake = new FakeSearchService { SearchException = expected };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RfcRagTools.SearchRfc(fake, "test", 10, CancellationToken.None));

        Assert.Same(expected, ex);
    }

    [Fact]
    public async Task SearchService_SearchAsync_LogsErrorBeforeThrow()
    {
        var logger = new RecordingLogger<SearchService>();
        SearchService search = await CreateSearchServiceWithDisposedDataSourceAsync(logger);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => search.SearchAsync("test", 10, CancellationToken.None));

        Assert.Contains(logger.Calls, call => call.Level == LogLevel.Error && call.Message.Contains("search_rfc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchService_SearchNormativeAsync_LogsErrorBeforeThrow()
    {
        var logger = new RecordingLogger<SearchService>();
        SearchService search = await CreateSearchServiceWithDisposedDataSourceAsync(logger);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => search.SearchNormativeAsync("MUST", null, 10, CancellationToken.None));

        Assert.Contains(logger.Calls, call => call.Level == LogLevel.Error && call.Message.Contains("search_normative", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchService_SearchAbnfAsync_LogsErrorBeforeThrow()
    {
        var logger = new RecordingLogger<SearchService>();
        SearchService search = await CreateSearchServiceWithDisposedDataSourceAsync(logger);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => search.SearchAbnfAsync("ALPHA", null, 10, CancellationToken.None));

        Assert.Contains(logger.Calls, call => call.Level == LogLevel.Error && call.Message.Contains("search_abnf", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchNormative_WhenSearchThrows_PropagatesException()
    {
        var expected = new InvalidOperationException("DB down");
        var fake = new FakeSearchService { SearchNormativeException = expected };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RfcRagTools.SearchNormative(fake, "MUST", null, 10, CancellationToken.None));

        Assert.Same(expected, ex);
    }

    [Fact]
    public async Task SearchAbnf_WhenSearchThrows_PropagatesException()
    {
        var expected = new InvalidOperationException("DB down");
        var fake = new FakeSearchService { SearchAbnfException = expected };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RfcRagTools.SearchAbnf(fake, "ALPHA", null, 10, CancellationToken.None));

        Assert.Same(expected, ex);
    }

    [Fact]
    public async Task GetRfcSection_Depth1_Leaf_ReturnsSingleSection()
    {
        var fake = new FakeSearchService
        {
            SectionWithChildren = new FakeSearchService.SectionTree
            {
                Parent = new RfcSection { RfcNumber = 8446, Section = "4.4.4", Heading = "Pre-Shared Key Exchange" },
                Children = []
            }
        };

        string json = await RfcRagTools.GetRfcSection(fake, 8446, "4.4.4", depth: 1, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("4.4.4", doc.RootElement.GetProperty("section").GetProperty("section").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("children").GetArrayLength());
    }

    private static async Task<SearchService> CreateSearchServiceWithDisposedDataSourceAsync(ILogger<SearchService> logger)
    {
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=postgres;Password=postgres;Database=postgres");
        await dataSource.DisposeAsync();

        return new SearchService(
            new SearchRepository(dataSource),
            new MetadataRepository(dataSource),
            CreateEmbeddingService(),
            logger);
    }

    private static EmbeddingService CreateEmbeddingService() =>
        new(new FakeEmbeddingGenerator(), 5, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Calls { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Calls.Add((logLevel, formatter(state, exception)));
    }
}
