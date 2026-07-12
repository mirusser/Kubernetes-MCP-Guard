namespace InfraGate.AuditOutbox;

/// <summary>
/// Read-only view of a single audit outbox stream. Implementations must not write,
/// re-hash, or otherwise mutate rows.
/// </summary>
public interface IAuditStreamReader
{
    /// <summary>
    /// Reads committed rows from <paramref name="streamSchema"/> whose <c>plan_id</c>
    /// correlation column equals <paramref name="planId"/>, ordered by audit sequence.
    /// </summary>
    Task<IReadOnlyList<AuditStreamRow>> ReadByPlanIdAsync(
        string streamSchema,
        string planId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads committed rows from <paramref name="streamSchema"/> whose <c>anomaly_id</c>
    /// correlation column equals <paramref name="anomalyId"/>, ordered by audit sequence.
    /// </summary>
    Task<IReadOnlyList<AuditStreamRow>> ReadByAnomalyIdAsync(
        string streamSchema,
        string anomalyId,
        CancellationToken cancellationToken);
}
