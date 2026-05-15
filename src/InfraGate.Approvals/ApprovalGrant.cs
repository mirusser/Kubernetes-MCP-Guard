namespace InfraGate.Approvals;

public sealed record ApprovalGrant(
    string Id,
    string PlanId,
    string RequesterSubject,
    string ApproverSubject,
    string SourceChallengeId,
    ApprovalDigest IntentDigest,
    ApprovalDigest ReviewDigest,
    ApprovalPolicy ApprovalPolicy,
    ExecutionReusePolicy ExecutionReusePolicy,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);
