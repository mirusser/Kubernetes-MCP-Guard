using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace InfraGate.RfcRag;

[McpServerToolType]
[Description("RFC RAG search and retrieval tools")]
public static class RfcRagTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    [McpServerTool(Name = "search_rfc", ReadOnly = true, OpenWorld = false)]
    [Description("Search RFCs using hybrid vector + full-text search. Returns ranked sections with excerpts.")]
    public static async Task<string> SearchRfc(
        ISearchService search,
        [Description("Search query for RFC content.")] string query,
        [Description("Maximum ranked sections to return, from 1 to 100.")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SearchResult> results = await search.SearchAsync(query, limit, cancellationToken).ConfigureAwait(false);
        return ToJson(results);
    }

    [McpServerTool(Name = "get_rfc", ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve the full text of an RFC by its number.")]
    public static async Task<string> GetRfc(
        ISearchService search,
        [Description("RFC number to retrieve.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RfcSection> sections = await search.GetRfcAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        if (sections.Count == 0)
        {
            return ToJson(new { error = $"RFC {rfcNumber} is not indexed." });
        }

        return ToJson(new
        {
            rfcNumber,
            title = sections[0].Title,
            sourcePath = sections[0].SourcePath,
            url = sections[0].Url,
            text = string.Join("\n\n", sections.Select(section => section.Text)),
            sections
        });
    }

    [McpServerTool(Name = "get_rfc_section", ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve a specific section of an RFC. Example section: '6.3'.")]
    public static async Task<string> GetRfcSection(
        ISearchService search,
        [Description("RFC number to retrieve from.")] int rfcNumber,
        [Description("Section number to retrieve, for example 6.3.")] string section,
        CancellationToken cancellationToken = default)
    {
        RfcSection? result = await search.GetSectionAsync(rfcNumber, section, cancellationToken).ConfigureAwait(false);
        return result is null
            ? ToJson(new { error = $"Section {section} of RFC {rfcNumber} is not indexed." })
            : ToJson(result);
    }

    [McpServerTool(Name = "search_normative", ReadOnly = true, OpenWorld = false)]
    [Description("Search for normative keywords (MUST, SHOULD, MAY, etc.) in RFCs. Keyword must be one of: MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY, OPTIONAL.")]
    public static async Task<string> SearchNormative(
        ISearchService search,
        [Description("Normative keyword to search for.")] string keyword,
        [Description("Optional RFC number filter.")] int[]? rfcNumbers = null,
        [Description("Maximum results to return, from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SearchResult> results = await search.SearchNormativeAsync(
            keyword,
            rfcNumbers,
            limit,
            cancellationToken).ConfigureAwait(false);
        return ToJson(results);
    }

    [McpServerTool(Name = "search_abnf", ReadOnly = true, OpenWorld = false)]
    [Description("Search for ABNF grammar definitions by rule name or fragment.")]
    public static async Task<string> SearchAbnf(
        ISearchService search,
        [Description("ABNF rule name or grammar fragment to search for.")] string query,
        [Description("Optional RFC number filter.")] int[]? rfcNumbers = null,
        [Description("Maximum results to return, from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SearchResult> results = await search.SearchAbnfAsync(
            query,
            rfcNumbers,
            limit,
            cancellationToken).ConfigureAwait(false);
        return ToJson(results);
    }

    [McpServerTool(Name = "find_updates_obsoletes", ReadOnly = true, OpenWorld = false)]
    [Description("Find RFCs that update or obsolete a given RFC (back-reference lookup).")]
    public static async Task<string> FindUpdatesObsoletes(
        ISearchService search,
        [Description("RFC number whose metadata relationships should be retrieved.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        RfcMetadata? metadata = await search.GetRfcMetadataAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return ToJson(new { error = $"RFC {rfcNumber} is not indexed." });
        }

        IReadOnlyList<RfcMetadata> backRefs = await search.FindBackReferencesAsync(rfcNumber, cancellationToken).ConfigureAwait(false);

        return ToJson(new
        {
            rfcNumber,
            metadata.Title,
            updates = metadata.Updates,
            obsoletes = metadata.Obsoletes,
            updated_by = backRefs
                .Where(r => !metadata.Obsoletes.Contains(r.Number))
                .Select(r => new { r.Number, r.Title })
                .ToArray(),
            obsoleted_by = backRefs
                .Where(r => metadata.Obsoletes.Contains(r.Number))
                .Select(r => new { r.Number, r.Title })
                .ToArray()
        });
    }

    [McpServerTool(Name = "rfc_stats", ReadOnly = true, OpenWorld = false)]
    [Description("Get statistics about the indexed RFC corpus.")]
    public static Task<string> RfcStats(
        ISearchService search,
        CancellationToken cancellationToken = default) =>
        search.GetStatsAsync(cancellationToken);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
}
