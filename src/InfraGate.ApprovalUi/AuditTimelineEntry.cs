namespace InfraGate.ApprovalUi;

/// <summary>
/// A single event on the audit timeline. Correlation columns are never exposed here;
/// only whitelisted display fields extracted from the payload.
/// </summary>
public sealed record class AuditTimelineEntry(
    DateTimeOffset OccurredAtUtc,
    string Stream,
    string EventName,
    string? ActorSubject,
    string? ActorClientId,
    string? Outcome,
    string? Reason,
    IReadOnlyDictionary<string, string?> DisplayFields);
