namespace InfraGate.AuditOutbox;

public sealed record class AuditOutboxRow(
    string EventName,
    DateTimeOffset OccurredAtUtc,
    string? ActorSubject,
    string? ActorClientId,
    string? Outcome,
    string? Reason,
    string PayloadJsonText,
    IReadOnlyDictionary<string, object?> CorrelationColumns);
