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

    public static class PlanStatusValues
    {
        public const string NotFound = "NotFound";
        public const string ApprovalRequired = "ApprovalRequired";
        public const string Approved = "Approved";
        public const string Applied = "Applied";
        public const string Expired = "Expired";
    }

    public static class AuditEvents
    {
        private const string ExecutionBlockedValue = "execution.blocked";

        public const string PlanRequested = "plan.created";
        public const string PreExecutionGrantValidated = "pre_execution.grant.validated";
        public const string PreExecutionChecked = "pre_execution.checked";
        public const string ExecutionStarted = "execution.started";
        public const string PlanApplied = "execution.succeeded";
        public const string ApplyDenied = ExecutionBlockedValue;
        public const string ApplyFailed = "execution.failed";
        public const string DryRunFailed = ExecutionBlockedValue;
        public const string DiffFailed = ExecutionBlockedValue;
        public const string ApplyDriftDetected = ExecutionBlockedValue;
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

    public static class ResultReasonCodes
    {
        public const string ChallengeAlreadyTerminal = "approval.challenge.already_terminal";
        public const string ChallengeExpired = "approval.challenge.expired";
        public const string ChallengeInvalid = "approval.challenge.invalid";
        public const string ChallengeNotFound = "approval.challenge.not_found";
        public const string DigestChanged = "approval.challenge.digest_changed";
        public const string GrantExpired = "approval.grant.expired";
        public const string InvalidGrant = "approval.grant.invalid";
        public const string InvalidPlanId = "approval.plan.invalid_id";
        public const string MissingReviewEvidence = "approval.review_evidence.missing";
        public const string PendingPlanChanged = "approval.challenge.pending_plan_changed";
        public const string PlanAlreadyApplied = "approval.plan.already_applied";
        public const string PlanExpired = "approval.plan.expired";
        public const string PlanNotApproved = "approval.plan.not_approved";
        public const string PlanNotPending = "approval.plan.not_pending";
        public const string PlanNotStarted = "approval.plan.not_started";
        public const string PlanReadFailed = "approval.plan.read_failed";
        public const string PlanUnsupportedFormat = "approval.plan.unsupported_format";
        public const string RequesterChanged = "approval.challenge.requester_changed";
    }
}
