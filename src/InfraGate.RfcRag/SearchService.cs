namespace InfraGate.RfcRag;

public sealed class SearchService : ISearchService
{
    private readonly NpgsqlDataSource dataSource;
    private readonly RfcRepository repository;
    private readonly EmbeddingService embeddingService;

    public SearchService(
        NpgsqlDataSource dataSource,
        RfcRepository repository,
        EmbeddingService embeddingService)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(embeddingService);

        this.dataSource = dataSource;
        this.repository = repository;
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

        return await repository.SearchHybridAsync(
            dataSource,
            query,
            embeddings[0],
            limit,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<RfcSection?> GetSectionAsync(int rfcNumber, string section, CancellationToken cancellationToken) =>
        repository.GetSectionAsync(dataSource, rfcNumber, section, cancellationToken);

    public Task<IReadOnlyList<RfcSection>> GetRfcAsync(int rfcNumber, CancellationToken cancellationToken) =>
        repository.GetRfcAsync(dataSource, rfcNumber, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        string keyword,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken) =>
        repository.SearchNormativeAsync(dataSource, keyword, rfcNumbers, limit, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        string query,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken) =>
        repository.SearchAbnfAsync(dataSource, query, rfcNumbers, limit, cancellationToken);

    public Task<RfcMetadata?> GetRfcMetadataAsync(int rfcNumber, CancellationToken cancellationToken) =>
        repository.GetIndexedRfcMetadataAsync(dataSource, rfcNumber, cancellationToken);

    public Task<IReadOnlyList<RfcMetadata>> FindBackReferencesAsync(int rfcNumber, CancellationToken cancellationToken) =>
        repository.FindBackReferencesAsync(dataSource, rfcNumber, cancellationToken);

    public Task<string> GetStatsAsync(CancellationToken cancellationToken) =>
        repository.GetStatsAsync(dataSource, cancellationToken);
}
