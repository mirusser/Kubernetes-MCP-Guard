using InfraGate.Approvals;
using InfraGate.Approvals.Audit;
using InfraGate.AuditOutbox;
using Npgsql;

namespace InfraGate.Approvals.Postgres;

internal interface ITransactionalApprovalAuditOutbox : IApprovalAuditOutbox
{
    Task<long> AppendAsync(
        ApprovalAuditEntry entry,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken);
}

internal sealed class ApprovalAuditOutbox(IAuditOutboxCore core, NpgsqlDataSource dataSource)
    : ITransactionalApprovalAuditOutbox
{
    private const string StreamSchema = PostgresApprovalConventions.Schema;

    public async Task<long> AppendAsync(ApprovalAuditEntry entry, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    var sequence = await core
                        .AppendAsync(StreamSchema, ToRow(entry), connection, transaction, cancellationToken)
                        .ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return sequence;
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    public Task<long> AppendAsync(
        ApprovalAuditEntry entry,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken) =>
        core.AppendAsync(StreamSchema, ToRow(entry), connection, transaction, cancellationToken);

    private static AuditOutboxRow ToRow(ApprovalAuditEntry entry) =>
        new(
            EventName: entry.EventName,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            ActorSubject: entry.ActorSubject,
            ActorClientId: entry.ActorClientId,
            Outcome: entry.Outcome,
            Reason: entry.Reason,
            PayloadJsonText: ApprovalCanonicalJson.Serialize(entry.Payload),
            CorrelationColumns: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["plan_id"] = entry.PlanId,
                ["challenge_id"] = entry.ChallengeId,
                ["grant_id"] = entry.GrantId,
                ["execution_attempt_id"] = entry.ExecutionAttemptId
            });
}
