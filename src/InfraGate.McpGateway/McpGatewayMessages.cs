namespace InfraGate.McpGateway;

internal static class McpGatewayMessages
{
    public static class ArgumentValidation
    {
        public const string MissingPlanId = "Missing required argument: planId.";
        public const string MissingOperationType = "Missing required argument: operationType.";
        public const string MissingArguments = "Missing required argument: arguments.";
        public const string TimeoutMustBeInteger = "timeoutSeconds must be an integer between 1 and 300.";
        public const string InvalidApprovalFormToken = "Invalid approval form token.";
    }

    public static class Authorization
    {
        public const string AuthenticatedSubjectRequired = "Approval requires an authenticated OAuth subject.";
        public const string CancelAuthenticatedSubjectRequired = "Approval cancellation requires an authenticated OAuth subject.";
        public const string CancelSameSubjectRequired = "Approval must be canceled by the same authenticated subject that requested it.";
        public const string CancelSubjectMismatch = "Canceling subject did not match requester subject.";
        public const string DenySameSubjectRequired = "Approval must be denied by the same authenticated subject that requested it.";
        public const string RequiresSameSubject = "Approval requires the same authenticated subject that requested the plan.";
        public const string RequiresOperatorGroup = "Approval requires membership in the configured operator group.";
        public const string ApproverNotInOperatorGroup = "Approver is not a member of the required operator group.";
        public const string ApproverSubjectMismatch = "Approver subject did not match requester subject.";
        public const string MutationRequiresAuth = "Refused: mutation plan creation requires an authenticated OAuth subject.";
        public const string ProposeRequiresAuth = "Refused: propose_plan requires an authenticated OAuth subject.";

        public static string RequiresSession(string toolName) =>
            $"Refused: '{toolName}' requires an authenticated session.";

        public const string RefusedAuthenticatedSubjectRequired =
            "Refused: apply approval requires an authenticated OAuth subject.";

        public const string RefusedSameSubjectRequired =
            "Refused: apply approval requires the same authenticated subject that requested the plan.";

        public static string RequiresAuthenticatedSession(string toolName, string scope) =>
            $"Refused: '{toolName}' requires an authenticated session with the '{scope}' scope.";

        public static string RequiresOneOfScopes(string toolName, string scopes) =>
            $"Refused: '{toolName}' requires an authenticated session with one of these scopes: {scopes}.";

        public static string RequiresScope(string toolName, string scope) =>
            $"Refused: tool '{toolName}' requires the '{scope}' scope.";

        public static string InvalidOperationType(string allowed) =>
            $"Refused: operationType must be one of: {allowed}.";
    }

    public static class ToolRouting
    {
        public static string DestructiveToolRequiresRequest(string toolName) =>
            $"Refused: destructive tool '{toolName}' must be requested through '{McpGatewayConventions.ToolNames.RequestToolPrefix}{toolName}' and executed with {McpGatewayConventions.ToolNames.ApplyApprovedPlan}.";

        public static string UnknownTool(string toolName) =>
            $"Unknown tool '{toolName}'.";

        public static string PlanCreated(string planId) =>
            $"Approval plan '{planId}' created. To execute, submit with {McpGatewayConventions.ToolNames.ApplyApprovedPlan}({McpGatewayConventions.ToolArguments.PlanId}=\"{planId}\").";
    }

    public static class Approval
    {
        public const string ChallengeNotFound = "Approval challenge was not found.";
        public const string ChallengeInvalid = "Approval challenge is invalid.";
        public const string ChallengeExpired = "Approval challenge expired. Ask the MCP client to request a new approval URL.";
        public const string ChallengeTtlExpired = "Challenge TTL expired.";
        public const string DigestBindingChanged = "The pending plan digest binding changed after this approval URL was created. Ask the MCP client to request a new approval URL.";
        public const string PendingPlanChanged = "The pending plan changed after this approval URL was created. Ask the MCP client to request a new approval URL.";
        public const string RequesterChanged = "The pending plan requester changed after this approval URL was created. Ask the MCP client to request a new approval URL.";
        public const string AdapterDecodeFailed = "Plan could not be decoded by the approval adapter.";

        public static string ChallengeAlreadyTerminal(string status) =>
            $"Approval challenge is already {status}.";

        public static string RefusedPlanNotStarted(string planId) =>
            $"Refused: plan '{planId}' validity window has not started yet.";

        public static string RefusedPlanExpired(string planId) =>
            $"Refused: plan '{planId}' has expired.";

        public static string AdapterDecodeFailedWithId(string planId) =>
            $"Plan '{planId}' could not be decoded by the approval adapter.";

        public static string PlanApproved(string planId, string grantId) =>
            $"Plan '{planId}' has been approved (grant: {grantId}). The Executor will apply the plan automatically.";

        public static string PlanDenied(string planId) =>
            $"Plan '{planId}' was denied.";

        public static string PlanCanceled(string planId) =>
            $"Plan '{planId}' approval challenge was canceled.";

        public static string MissingEvidence(string planId) =>
            $"Plan '{planId}' is missing recorded evidence data. Ask the MCP client to re-request the plan.";

        public static string PlanExecutionFailed(string planId, string message) =>
            $"Plan '{planId}' execution failed: {message}";
    }
}
