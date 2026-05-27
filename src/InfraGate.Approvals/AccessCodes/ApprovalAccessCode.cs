namespace InfraGate.Approvals.AccessCodes;

public sealed record class ApprovalAccessCode(
    string Code,
    string ChallengeId,
    DateTimeOffset ExpiresAtUtc);
