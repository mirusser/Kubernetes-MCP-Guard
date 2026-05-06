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
        public const string ApprovedDirectory = "approved";
        public const string AppliedDirectory = "applied";
        public const string ChallengesDirectory = "challenges";
        public const string AuditFileName = "audit.jsonl";
        public const string DefaultRootDirectory = ".mcp-approvals";
        public const string JsonExtension = ".json";
        public const string Sha256Extension = ".sha256";
    }

    public static class AuditEvents
    {
        public const string PlanRequested = "plan_requested";
        public const string ApprovalHashMismatch = "approval_hash_mismatch";
        public const string PlanApproved = "plan_approved";
        public const string PlanApplied = "plan_applied";
        public const string ApplyDenied = "apply_denied";
        public const string ApplyFailed = "apply_failed";
        public const string ApprovalChallengeCreated = "approval_challenge_created";
        public const string ApprovalChallengeApproved = "approval_challenge_approved";
        public const string ApprovalChallengeDenied = "approval_challenge_denied";
        public const string ApprovalChallengeExpired = "approval_challenge_expired";
        public const string ApprovalChallengeRejected = "approval_challenge_rejected";
    }

    public static class ChallengeStatuses
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Denied = "denied";
        public const string Expired = "expired";
    }

    public static class ApprovalSources
    {
        public const string GatewayOutOfBand = "gateway_oob";
        public const string DirectStore = "direct_store";
    }

    public static class DateTimeFormats
    {
        public const string PlanIdTimestamp = "yyyyMMddHHmmss";
        public const string RoundTrip = "O";
    }
}
