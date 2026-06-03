using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using Npgsql;

namespace InfraGate.Observer.Audit;

internal sealed class ObserverAuditOutbox(IPostgresAuditOutboxCore core, NpgsqlDataSource dataSource)
    : IObserverAuditOutbox
{
    private const string StreamSchema = AuditOutboxConventions.Streams.Observer;
    private const string ColCycleId = "cycle_id";
    private const string ColAnomalyId = "anomaly_id";
    private const string ColDedupeKey = "dedupe_key";

    public async Task<long> AppendAsync(ObserverAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                long sequence = await core
                    .AppendAsync(StreamSchema, ToRow(entry), connection, transaction, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return sequence;
            }
        }
    }

    public Task<long> AppendAsync(
        ObserverAuditEntry entry,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return core.AppendAsync(StreamSchema, ToRow(entry), connection, transaction, cancellationToken);
    }

    private static AuditOutboxRow ToRow(ObserverAuditEntry entry) =>
        new(
            EventName: entry.EventName,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            ActorSubject: entry.ActorSubject,
            ActorClientId: entry.ActorClientId,
            Outcome: entry.Outcome,
            Reason: entry.Reason,
            PayloadJsonText: CanonicalJson.Serialize(entry.Payload),
            CorrelationColumns: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ColCycleId] = entry.CycleId,
                [ColAnomalyId] = entry.AnomalyId,
                [ColDedupeKey] = entry.DedupeKey,
            });
}
