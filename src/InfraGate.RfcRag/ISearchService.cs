namespace InfraGate.RfcRag;

public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    Task<RfcSection?> GetSectionAsync(int rfcNumber, string section, CancellationToken cancellationToken);

    Task<IReadOnlyList<RfcSection>> GetRfcAsync(int rfcNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        string keyword,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        string query,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken);

    Task<string> GetStatsAsync(CancellationToken cancellationToken);
}
