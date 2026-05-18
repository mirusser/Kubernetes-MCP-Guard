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
        public const string DataProtectionKeysDirectory = "dataprotection-keys";
        public const string AuditFileName = "audit.jsonl";
        public const string DefaultRootDirectory = ".mcp-approvals";
        public const string JsonExtension = ".json";
        public const string Sha256Extension = ".sha256";
    }

    public static class Application
    {
        public const string Name = "InfraGate";
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
        public const string PlanRequested = "plan.created";
        public const string PlanApplied = "execution.succeeded";
        public const string ApplyDenied = "execution.blocked";
        public const string ApplyFailed = "execution.failed";
        public const string DryRunFailed = "execution.blocked";
        public const string DiffFailed = "execution.blocked";
        public const string ApplyDriftDetected = "execution.blocked";
        public const string ApprovalChallengeCreated = "challenge.created";
        public const string ApprovalChallengeApproved = "challenge.approved";
        public const string ApprovalChallengeDenied = "challenge.denied";
        public const string ApprovalChallengeExpired = "challenge.expired";
        public const string ApprovalChallengeRejected = "challenge.rejected";
        public const string ApprovalChallengeCanceled = "challenge.canceled";
        public const string GrantIssued = "grant.issued";
    }

    public static class ChallengeStatuses
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Denied = "denied";
        public const string Expired = "expired";
        public const string Rejected = "rejected";
        public const string Canceled = "canceled";
    }

    public static class ChallengeOutcomeStatuses
    {
        public const string Approved = "approved";
        public const string Denied = "denied";
        public const string Expired = "expired";
        public const string Rejected = "rejected";
        public const string Canceled = "canceled";
    }

    public static class PolicySeverities
    {
        public const string Information = "Information";
        public const string Warning = "Warning";
        public const string Error = "Error";
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
