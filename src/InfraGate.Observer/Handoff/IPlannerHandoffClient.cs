namespace InfraGate.Observer.Handoff;

internal interface IPlannerHandoffClient
{
    Task SendAsync(
        string contextId,
        AnomalyHandoffBatch batch,
        CancellationToken cancellationToken);
}
