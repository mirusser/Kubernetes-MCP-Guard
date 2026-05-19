// Audit-payload records for approval-challenge-anchored events written to
// audit.jsonl. See PlanAuditPayloads.cs for the rationale on grouped-file
// layout, marker-interface choice, and the parameter-name-to-JSON-key contract.

namespace InfraGate.Approvals.AuditPayloads;

public interface IChallengeAuditPayload
{
    string Id { get; }

    string PlanId { get; }
}

public sealed record ApprovalChallengeCreatedPayload(
    string Id,
    string PlanId,
    string PendingPlanHash,
    string RequesterSubject,
    string? RequesterAuthenticationType,
    DateTimeOffset ExpiresAtUtc) : IChallengeAuditPayload;

public sealed record ApprovalChallengeApprovedPayload(
    string Id,
    string PlanId,
    string PendingPlanHash,
    string RequesterSubject,
    string? ApproverSubject,
    DateTimeOffset DecidedAt) : IChallengeAuditPayload;

public sealed record ApprovalChallengeDeniedPayload(
    string Id,
    string PlanId,
    string PendingPlanHash,
    string RequesterSubject,
    string? ApproverSubject,
    DateTimeOffset DecidedAt) : IChallengeAuditPayload;

public sealed record ApprovalChallengeExpiredPayload(
    string Id,
    string PlanId,
    string PendingPlanHash,
    string RequesterSubject,
    DateTimeOffset ExpiresAtUtc) : IChallengeAuditPayload;

public sealed record ApprovalChallengeRejectedPayload(
    string Id,
    string PlanId,
    string PendingPlanHash,
    string RequesterSubject,
    string? ApproverSubject,
    string Reason) : IChallengeAuditPayload;

public sealed record ApprovalChallengeCanceledPayload(
    string Id,
    string PlanId,
    string PendingPlanHash,
    string RequesterSubject,
    string? ActorSubject,
    DateTimeOffset DecidedAt) : IChallengeAuditPayload;
