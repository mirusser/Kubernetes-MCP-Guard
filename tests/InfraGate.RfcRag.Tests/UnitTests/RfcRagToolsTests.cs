using System.Text.Json;
using InfraGate.RfcRag.Models;
using InfraGate.RfcRag.Search;
using InfraGate.RfcRag.Tests.Fakes;
using InfraGate.RfcRag.Tools;

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
    public async Task GetRfc_WithSections_ReturnsFullText()
    {
        var fake = new FakeSearchService
        {
            RfcSections = [new RfcSection
            {
                Id = Guid.NewGuid(),
                RfcNumber = 2119,
                Title = "Key words",
                Section = "1",
                Heading = "Introduction",
                Text = "The key words MUST",
                SourcePath = "rfc2119.txt",
                Url = "https://www.rfc-editor.org/rfc/rfc2119"
            }]
        };

        string json = await RfcRagTools.GetRfc(fake, 2119, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(2119, root.GetProperty("rfcNumber").GetInt32());
        Assert.True(root.TryGetProperty("text", out _));
        Assert.True(root.TryGetProperty("sections", out _));
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
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(2119, root.GetProperty("rfcNumber").GetInt32());
        Assert.Equal("1", root.GetProperty("section").GetString());
    }

    [Fact]
    public async Task GetRfcSection_NullResult_ReturnsError()
    {
        var fake = new FakeSearchService { SingleSection = null };

        string json = await RfcRagTools.GetRfcSection(fake, 9999, "99", CancellationToken.None);

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
}
