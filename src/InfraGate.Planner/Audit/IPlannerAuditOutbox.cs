using Npgsql;

namespace InfraGate.Planner.Audit;

internal interface IPlannerAuditOutbox
{
    Task<long> AppendAsync(PlannerAuditEntry entry, CancellationToken cancellationToken);

    Task<long> AppendAsync(
        PlannerAuditEntry entry,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken);
}
