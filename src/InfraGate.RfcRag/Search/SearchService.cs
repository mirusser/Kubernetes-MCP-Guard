using InfraGate.RfcRag.Indexing;
using InfraGate.RfcRag.Models;
using Microsoft.Extensions.Logging;

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
            return [];
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
            return [];
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
            return [];
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
}
