using System.ComponentModel;
using System.Text.Json;
using InfraGate.RfcRag.Search;
using ModelContextProtocol.Server;

namespace InfraGate.RfcRag.Tools;

[McpServerToolType]
[Description("RFC RAG search and retrieval tools")]
public static class RfcRagTools
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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
    [Description("Retrieve RFC metadata, table of contents, and a preview of the first sections.")]
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

        const int previewLimit = 20;
        var toc = sections.ToDictionary(s => s.Section, s => s.Heading, StringComparer.Ordinal);
        IReadOnlyList<RfcSection> previewSections = sections.Take(previewLimit).ToArray();

        return ToJson(new
        {
            rfcNumber,
            title = sections[0].Title,
            sourcePath = sections[0].SourcePath,
            url = sections[0].Url,
            sectionCount = sections.Count,
            toc,
            sections = previewSections
        });
    }

    [McpServerTool(Name = "get_rfc_full", ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve the full concatenated text of an RFC. Use sparingly — output can be very large.")]
    public static async Task<string> GetRfcFull(
        ISearchService search,
        [Description("RFC number to retrieve.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RfcSection> sections = await search.GetRfcAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        if (sections.Count == 0)
        {
            return ToJson(new { error = $"RFC {rfcNumber} is not indexed." });
        }

        string fullText = string.Join("\n\n", sections.Select(s => s.Text));

        return ToJson(new
        {
            rfcNumber,
            title = sections[0].Title,
            sourcePath = sections[0].SourcePath,
            url = sections[0].Url,
            sectionCount = sections.Count,
            text = fullText
        });
    }

    [McpServerTool(Name = "get_rfc_section", ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve a specific section of an RFC. Example section: '6.3'. Set depth=1 to include child sections (when depth>0, expand is ignored). Set expand=true to resolve type references when depth=0.")]
    public static async Task<string> GetRfcSection(
        ISearchService search,
        [Description("RFC number to retrieve from.")] int rfcNumber,
        [Description("Section number to retrieve, for example 6.3.")] string section,
        [Description("Number of child levels to include (0 = section only, 1 = immediate children).")] int depth = 0,
        [Description("When true, resolves PascalCase type references to their defining sections.")] bool expand = false,
        CancellationToken cancellationToken = default)
    {
        if (depth > 0)
        {
            (RfcSection parent, IReadOnlyList<RfcSection> children) = await search.GetSectionWithChildrenAsync(
                rfcNumber, section, depth, cancellationToken).ConfigureAwait(false);

            if (parent.Section.Length == 0)
                return ToJson(new { error = $"Section {section} of RFC {rfcNumber} is not indexed." });

            return ToJson(new { section = parent, children });
        }

        RfcSection? result = await search.GetSectionAsync(rfcNumber, section, cancellationToken).ConfigureAwait(false);
        if (result is null)
            return ToJson(new { error = $"Section {section} of RFC {rfcNumber} is not indexed." });

        if (expand)
        {
            IReadOnlyDictionary<string, RfcSection> expandedTypes = await search.GetSectionWithExpandedTypesAsync(
                rfcNumber, section, cancellationToken).ConfigureAwait(false);

            return expandedTypes.Count > 0
                ? ToJson(new { section = result, expandedTypes })
                : ToJson(result);
        }

        return ToJson(result);
    }

    [McpServerTool(Name = "get_rfc_metadata", ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve metadata for a specific RFC (title, updates, obsoletes).")]
    public static async Task<string> GetRfcMetadata(
        ISearchService search,
        [Description("RFC number to retrieve metadata for.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        RfcMetadata? metadata = await search.GetRfcMetadataAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        return metadata is null
            ? ToJson(new { error = $"RFC {rfcNumber} is not indexed." })
            : ToJson(metadata);
    }

    [McpServerTool(Name = "list_indexed_rfcs", ReadOnly = true, OpenWorld = false)]
    [Description("List indexed RFCs with their numbers and titles.")]
    public static async Task<string> ListIndexedRfcs(
        ISearchService search,
        [Description("Maximum results to return, from 1 to 1000.")] int limit = 100,
        [Description("Number of results to skip for pagination.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RfcMetadata> rfcs = await search.ListIndexedAsync(limit, offset, cancellationToken).ConfigureAwait(false);
        return ToJson(new { total = rfcs.Count, rfcs });
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
                .Where(r => r.Updates.Contains(rfcNumber))
                .Select(r => new { r.Number, r.Title })
                .ToArray(),
            obsoleted_by = backRefs
                .Where(r => r.Obsoletes.Contains(rfcNumber))
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

    [McpServerTool(Name = "get_rfc_toc", ReadOnly = true, OpenWorld = false)]
    [Description("Get the table of contents for an RFC as a flat section→heading map.")]
    public static async Task<string> GetRfcToc(
        ISearchService search,
        [Description("RFC number to retrieve TOC for.")] int rfcNumber,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, string?> toc = await search.GetTocAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        if (toc.Count == 0)
        {
            return ToJson(new { error = $"RFC {rfcNumber} is not indexed." });
        }

        return ToJson(toc);
    }

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, jsonOptions);
}
