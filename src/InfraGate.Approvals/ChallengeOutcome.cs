namespace InfraGate.Approvals;

public sealed record class ChallengeOutcome(
    string Status,
    string? ActorSubject,
    DateTimeOffset DecidedAtUtc,
    string? Reason,
    string? GrantId);
