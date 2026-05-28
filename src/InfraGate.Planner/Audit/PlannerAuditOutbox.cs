using InfraGate.AuditOutbox;
using InfraGate.AuditOutbox.Postgres;
using Npgsql;

namespace InfraGate.Planner.Audit;

internal sealed class PlannerAuditOutbox(IAuditOutboxCore core, NpgsqlDataSource dataSource)
    : IPlannerAuditOutbox
{
    private const string StreamSchema = AuditOutboxConventions.Streams.Planner;
    private const string ColProposalId = "proposal_id";
    private const string ColAnomalyId = "anomaly_id";
    private const string ColPlanId = "plan_id";

    public async Task<long> AppendAsync(PlannerAuditEntry entry, CancellationToken cancellationToken)
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
        PlannerAuditEntry entry,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return core.AppendAsync(StreamSchema, ToRow(entry), connection, transaction, cancellationToken);
    }

    private static AuditOutboxRow ToRow(PlannerAuditEntry entry) =>
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
                [ColProposalId] = entry.ProposalId,
                [ColAnomalyId] = entry.AnomalyId,
                [ColPlanId] = entry.PlanId,
            });
}
