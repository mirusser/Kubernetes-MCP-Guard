using InfraGate.RfcRag.Models;
using InfraGate.RfcRag.Search;

namespace InfraGate.RfcRag.Tests.Fakes;

internal sealed class FakeSearchService : ISearchService
{
    public IReadOnlyList<SearchResult> SearchResults { get; set; } = [];
    public IReadOnlyList<RfcSection> RfcSections { get; set; } = [];
    public RfcSection? SingleSection { get; set; }
    public RfcMetadata? Metadata { get; set; }
    public IReadOnlyList<RfcMetadata> BackReferences { get; set; } = [];
    public string StatsJson { get; set; } = """{"indexedRfcs":0,"sections":0,"abnfBlocks":0,"normativeOccurrences":0,"lastIndexedAtUtc":null}""";
    public IReadOnlyList<RfcMetadata> IndexedRfcList { get; set; } = [];

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(SearchResults);

    public Task<RfcSection?> GetSectionAsync(
        int rfcNumber, string section, CancellationToken cancellationToken) =>
        Task.FromResult(SingleSection);

    public Task<IReadOnlyList<RfcSection>> GetRfcAsync(
        int rfcNumber, CancellationToken cancellationToken) =>
        Task.FromResult(RfcSections);

    public Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        string keyword, int[]? rfcNumbers, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(SearchResults);

    public Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        string query, int[]? rfcNumbers, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(SearchResults);

    public Task<RfcMetadata?> GetRfcMetadataAsync(
        int rfcNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Metadata);

    public Task<IReadOnlyList<RfcMetadata>> FindBackReferencesAsync(
        int rfcNumber, CancellationToken cancellationToken) =>
        Task.FromResult(BackReferences);

    public Task<IReadOnlyList<RfcMetadata>> ListIndexedAsync(
        int limit, int offset, CancellationToken cancellationToken) =>
        Task.FromResult(IndexedRfcList);

    public Task<string> GetStatsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(StatsJson);
}
