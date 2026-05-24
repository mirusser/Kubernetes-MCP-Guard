namespace InfraGate.Observer.Snapshot;

internal interface ISnapshotFetcher
{
    Task<SnapshotDocument> FetchAsync(string namespaceName, CancellationToken cancellationToken);
}
