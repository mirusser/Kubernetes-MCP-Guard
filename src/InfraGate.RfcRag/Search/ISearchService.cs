using InfraGate.RfcRag.Models;

namespace InfraGate.RfcRag.Search;

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

    Task<RfcMetadata?> GetRfcMetadataAsync(int rfcNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<RfcMetadata>> FindBackReferencesAsync(int rfcNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<RfcMetadata>> ListIndexedAsync(int limit, int offset, CancellationToken cancellationToken);

    Task<string> GetStatsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string?>> GetTocAsync(int rfcNumber, CancellationToken cancellationToken);

    Task<(RfcSection Parent, IReadOnlyList<RfcSection> Children)> GetSectionWithChildrenAsync(
        int rfcNumber, string section, int depth, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, RfcSection>> GetSectionWithExpandedTypesAsync(
        int rfcNumber, string section, CancellationToken cancellationToken);
}
