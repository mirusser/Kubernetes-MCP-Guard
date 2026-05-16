namespace InfraGate.Approvals;

public static class ApprovalConventions
{
    public static class EnvironmentVariables
    {
        public const string ApprovalRoot = "K8S_MCP_APPROVAL_ROOT";
    }

    public static class Storage
    {
        public const string PendingDirectory = "pending";
        public const string AppliedDirectory = "applied";
        public const string ChallengesDirectory = "challenges";
        public const string GrantsDirectory = "grants";
        public const string AuditFileName = "audit.jsonl";
        public const string DefaultRootDirectory = ".mcp-approvals";
        public const string JsonExtension = ".json";
        public const string Sha256Extension = ".sha256";
    }

    public static class Profiles
    {
        public const string MutationApproval = "mcp.mutation-approval";
    }

    public static class Digests
    {
        public const string Sha256 = "sha-256";
    }

    public static class Canonicalizations
    {
        public const string ProfileReviewV1 = "infra-gate.approval.review.v1";
    }

    public static class ReviewSurfaces
    {
        public const string GatewayBrowser = "gateway-browser";
    }

    public static class ApprovalPolicyTypes
    {
        public const string SameSubject = "same-subject";
    }

    public static class ExecutionReusePolicyTypes
    {
        public const string SingleExecution = "single-execution";
    }

    public static class PlanValidity
    {
        public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(1);
    }

    public static class AuditEvents
    {
        public const string PlanRequested = "plan_requested";
        public const string PlanApplied = "plan_applied";
        public const string ApplyDenied = "apply_denied";
        public const string ApplyFailed = "apply_failed";
        public const string DryRunFailed = "dry_run_failed";
        public const string DiffFailed = "diff_failed";
        public const string ApplyDriftDetected = "apply_drift_detected";
        public const string ApprovalChallengeCreated = "approval_challenge_created";
        public const string ApprovalChallengeApproved = "approval_challenge_approved";
        public const string ApprovalChallengeDenied = "approval_challenge_denied";
        public const string ApprovalChallengeExpired = "approval_challenge_expired";
        public const string ApprovalChallengeRejected = "approval_challenge_rejected";
        public const string GrantIssued = "grant_issued";
    }

    public static class ChallengeStatuses
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Denied = "denied";
        public const string Expired = "expired";
        public const string Rejected = "rejected";
    }

    public static class ChallengeOutcomeStatuses
    {
        public const string Approved = "approved";
        public const string Denied = "denied";
        public const string Expired = "expired";
        public const string Rejected = "rejected";
    }

    public static class DiffChangeTypes
    {
        public const string Create = "create";
        public const string Update = "update";
        public const string Delete = "delete";
        public const string NoOp = "no-op";
    }

    public static class DateTimeFormats
    {
        public const string RoundTrip = "O";
    }
}
