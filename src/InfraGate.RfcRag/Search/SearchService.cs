namespace InfraGate.RfcRag.Search;

public sealed class SearchService : ISearchService
{
    private readonly SearchRepository searchRepository;
    private readonly MetadataRepository metadataRepository;
    private readonly EmbeddingService embeddingService;
    private readonly ILogger<SearchService> logger;

    public SearchService(
        SearchRepository searchRepository,
        MetadataRepository metadataRepository,
        EmbeddingService embeddingService,
        ILogger<SearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(searchRepository);
        ArgumentNullException.ThrowIfNull(metadataRepository);
        ArgumentNullException.ThrowIfNull(embeddingService);
        ArgumentNullException.ThrowIfNull(logger);

        this.searchRepository = searchRepository;
        this.metadataRepository = metadataRepository;
        this.embeddingService = embeddingService;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        try
        {
            IReadOnlyList<float[]> embeddings = await embeddingService.GenerateEmbeddingsAsync(
                [query],
                cancellationToken).ConfigureAwait(false);

            return await searchRepository.SearchHybridAsync(
                query,
                embeddings[0],
                limit,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "search_rfc failed for query={Query}", query);
            throw;
        }
    }

    public Task<RfcSection?> GetSectionAsync(int rfcNumber, string section, CancellationToken cancellationToken) =>
        searchRepository.GetSectionAsync(rfcNumber, section, cancellationToken);

    public Task<IReadOnlyList<RfcSection>> GetRfcAsync(int rfcNumber, CancellationToken cancellationToken) =>
        searchRepository.GetRfcAsync(rfcNumber, cancellationToken);

    public async Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        string keyword,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return await searchRepository.SearchNormativeAsync(keyword, rfcNumbers, limit, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "search_normative failed for keyword={Keyword}", keyword);
            throw;
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        string query,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return await searchRepository.SearchAbnfAsync(query, rfcNumbers, limit, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "search_abnf failed for query={Query}", query);
            throw;
        }
    }

    public Task<RfcMetadata?> GetRfcMetadataAsync(int rfcNumber, CancellationToken cancellationToken) =>
        metadataRepository.GetIndexedRfcMetadataAsync(rfcNumber, cancellationToken);

    public Task<IReadOnlyList<RfcMetadata>> FindBackReferencesAsync(int rfcNumber, CancellationToken cancellationToken) =>
        metadataRepository.FindBackReferencesAsync(rfcNumber, cancellationToken);

    public Task<IReadOnlyList<RfcMetadata>> ListIndexedAsync(int limit, int offset, CancellationToken cancellationToken) =>
        metadataRepository.ListIndexedAsync(limit, offset, cancellationToken);

    public Task<string> GetStatsAsync(CancellationToken cancellationToken) =>
        metadataRepository.GetStatsAsync(cancellationToken);

    public Task<IReadOnlyDictionary<string, string?>> GetTocAsync(
        int rfcNumber,
        CancellationToken cancellationToken) =>
        searchRepository.GetTocAsync(rfcNumber, cancellationToken);

    public Task<(RfcSection Parent, IReadOnlyList<RfcSection> Children)> GetSectionWithChildrenAsync(
        int rfcNumber,
        string section,
        int depth,
        CancellationToken cancellationToken) =>
        searchRepository.GetSectionWithChildrenAsync(rfcNumber, section, depth, cancellationToken);

    public async Task<IReadOnlyDictionary<string, RfcSection>> GetSectionWithExpandedTypesAsync(
        int rfcNumber,
        string section,
        CancellationToken cancellationToken)
    {
        RfcSection? target = await searchRepository.GetSectionAsync(rfcNumber, section, cancellationToken).ConfigureAwait(false);
        if (target is null)
            return new Dictionary<string, RfcSection>(StringComparer.Ordinal);

        var typeNames = ExtractPascalCaseTypeNames(target.Text, target.Heading);
        if (typeNames.Count == 0)
            return new Dictionary<string, RfcSection>(StringComparer.Ordinal);

        IReadOnlyList<RfcSection> rfcSections = await searchRepository.GetRfcAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        var knownHeadings = rfcSections
            .Where(rfcSection => rfcSection.Heading is not null)
            .Select(rfcSection => rfcSection.Heading!)
            .ToHashSet(StringComparer.Ordinal);

        string[] matchingTypeNames = typeNames
            .Where(knownHeadings.Contains)
            .ToArray();

        return await searchRepository.FindSectionsByHeadingsAsync(rfcNumber, matchingTypeNames, cancellationToken).ConfigureAwait(false);
    }

    internal static List<string> ExtractPascalCaseTypeNames(string sectionText, string? sectionHeading)
    {
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        var words = sectionText.Split(new[] { ' ', '\n', '\r', '\t', '(', ')', '{', '}', ',', '.', ':', ';', '[', ']' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string word in words)
        {
            if (word.Length >= 2 && char.IsUpper(word[0]) && word.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                typeNames.Add(word);
            }
        }

        if (sectionHeading is not null)
            typeNames.Remove(sectionHeading);

        return typeNames.ToList();
    }
}
