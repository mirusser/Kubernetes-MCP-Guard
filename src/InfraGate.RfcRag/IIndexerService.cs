namespace InfraGate.RfcRag;

public interface IIndexerService
{
    Task IndexAllAsync(CancellationToken cancellationToken);

    Task<int> GetIndexedCountAsync(CancellationToken cancellationToken);
}
