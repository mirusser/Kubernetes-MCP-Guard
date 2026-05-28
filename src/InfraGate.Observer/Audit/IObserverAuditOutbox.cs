using Npgsql;

namespace InfraGate.Observer.Audit;

internal interface IObserverAuditOutbox
{
    Task<long> AppendAsync(ObserverAuditEntry entry, CancellationToken cancellationToken);

    Task<long> AppendAsync(
        ObserverAuditEntry entry,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken);
}
