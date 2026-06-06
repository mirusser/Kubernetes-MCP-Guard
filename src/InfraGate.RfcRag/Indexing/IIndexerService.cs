namespace InfraGate.RfcRag.Indexing;

public interface IIndexerService
{
    Task IndexAllAsync(CancellationToken cancellationToken);

    Task IndexSingleAsync(int rfcNumber, bool force, CancellationToken cancellationToken);

    Task<int> GetIndexedCountAsync(CancellationToken cancellationToken);
}
