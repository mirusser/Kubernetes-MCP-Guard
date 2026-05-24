namespace InfraGate.Observer.Contracts;

public interface IAnomalyHandoffSink
{
    Task PublishAsync(AnomalyHandoffBatch batch, CancellationToken cancellationToken);
}
