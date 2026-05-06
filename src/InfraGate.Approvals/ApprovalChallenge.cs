namespace InfraGate.Approvals;

public sealed record ApprovalChallenge(
    string Id,
    string PlanId,
    string PlanHash,
    string RequesterSubject,
    string? RequesterAuthenticationType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status,
    string? ApproverSubject,
    DateTimeOffset? DecidedAtUtc);
