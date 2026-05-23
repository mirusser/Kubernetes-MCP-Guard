namespace InfraGate.ApprovalUi;

public sealed record class ApprovalChallengeInfo(
    string ChallengeId,
    string PlanId,
    string RequesterSubject,
    string? RequesterAuthenticationType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status);
