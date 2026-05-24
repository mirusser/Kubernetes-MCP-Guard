namespace InfraGate.Approvals;

public sealed record class ApprovalAccessCode(
    string Code,
    string ChallengeId,
    DateTimeOffset ExpiresAtUtc);
