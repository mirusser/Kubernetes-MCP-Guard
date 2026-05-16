namespace InfraGate.Approvals;

public sealed record ApprovalChallenge(
    string Id,
    string PlanId,
    string PendingPlanHash,
    string RequesterSubject,
    string? RequesterAuthenticationType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status,
    string? ApproverSubject,
    DateTimeOffset? DecidedAtUtc,
    ApprovalDigest IntentDigest,
    ApprovalDigest ReviewDigest,
    ChallengeOutcome? Outcome = null);
