namespace InfraGate.Approvals;

public sealed record ChallengeOutcome(
    string Status,
    string? ActorSubject,
    DateTimeOffset DecidedAtUtc,
    string? Reason,
    string? GrantId);
