namespace InfraGate.AuditOutbox;

/// <summary>
/// A single row returned by <see cref="IAuditStreamReader"/>. Carries the outbox
/// sequence number alongside the domain row.
/// </summary>
/// <param name="AuditSequence">The monotonic sequence number in the stream.</param>
/// <param name="Row">The audit row contents.</param>
public sealed record class AuditStreamRow(long AuditSequence, AuditOutboxRow Row);
