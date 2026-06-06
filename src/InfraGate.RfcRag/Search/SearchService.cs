using InfraGate.RfcRag.Indexing;
using InfraGate.RfcRag.Models;

namespace InfraGate.RfcRag.Search;

public sealed class SearchService : ISearchService
{
    private readonly SearchRepository searchRepository;
    private readonly MetadataRepository metadataRepository;
    private readonly EmbeddingService embeddingService;

    public SearchService(
        SearchRepository searchRepository,
        MetadataRepository metadataRepository,
        EmbeddingService embeddingService)
    {
        ArgumentNullException.ThrowIfNull(searchRepository);
        ArgumentNullException.ThrowIfNull(metadataRepository);
        ArgumentNullException.ThrowIfNull(embeddingService);

        this.searchRepository = searchRepository;
        this.metadataRepository = metadataRepository;
        this.embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        IReadOnlyList<float[]> embeddings = await embeddingService.GenerateEmbeddingsAsync(
            [query],
            cancellationToken).ConfigureAwait(false);

        return await searchRepository.SearchHybridAsync(
            query,
            embeddings[0],
            limit,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<RfcSection?> GetSectionAsync(int rfcNumber, string section, CancellationToken cancellationToken) =>
        searchRepository.GetSectionAsync(rfcNumber, section, cancellationToken);

    public Task<IReadOnlyList<RfcSection>> GetRfcAsync(int rfcNumber, CancellationToken cancellationToken) =>
        searchRepository.GetRfcAsync(rfcNumber, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        string keyword,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken) =>
        searchRepository.SearchNormativeAsync(keyword, rfcNumbers, limit, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        string query,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken) =>
        searchRepository.SearchAbnfAsync(query, rfcNumbers, limit, cancellationToken);

    public Task<RfcMetadata?> GetRfcMetadataAsync(int rfcNumber, CancellationToken cancellationToken) =>
        metadataRepository.GetIndexedRfcMetadataAsync(rfcNumber, cancellationToken);

    public Task<IReadOnlyList<RfcMetadata>> FindBackReferencesAsync(int rfcNumber, CancellationToken cancellationToken) =>
        metadataRepository.FindBackReferencesAsync(rfcNumber, cancellationToken);

    public Task<IReadOnlyList<RfcMetadata>> ListIndexedAsync(int limit, int offset, CancellationToken cancellationToken) =>
        metadataRepository.ListIndexedAsync(limit, offset, cancellationToken);

    public Task<string> GetStatsAsync(CancellationToken cancellationToken) =>
        metadataRepository.GetStatsAsync(cancellationToken);
}
