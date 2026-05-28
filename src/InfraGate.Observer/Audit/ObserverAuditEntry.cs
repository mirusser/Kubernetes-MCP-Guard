namespace InfraGate.Observer.Audit;

internal sealed record class ObserverAuditEntry(
    string EventName,
    object Payload,
    string? CycleId = null,
    string? AnomalyId = null,
    string? DedupeKey = null,
    string? ActorSubject = null,
    string? ActorClientId = null,
    string? Outcome = null,
    string? Reason = null);
