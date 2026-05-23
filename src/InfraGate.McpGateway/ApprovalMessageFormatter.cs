using InfraGate.Approvals;
using InfraGate.KubernetesAdapter;

namespace InfraGate.McpGateway;

internal static class ApprovalMessageFormatter
{
    public static string RenderApprovalRequiredMessage(
        IPlanReview planReview,
        string approvalUrl,
        DateTimeOffset expiresAtUtc)
    {
        var targets = string.Join(
            Environment.NewLine,
            planReview.Targets.Select(t =>
                $"  - {t.Attributes.GetValueOrDefault(KubernetesAdapterConventions.PlanAttributeKeys.ApiVersion, "?")} {t.Type} {t.Scope}/{t.Name}"));

        return $"""
               Approval required.
               PlanId: {planReview.Envelope.Id}
               Operation: {planReview.Envelope.Operation}
               Description: {planReview.Description}
               Targets:
               {targets}
               Intent Digest: {planReview.Envelope.IntentDigest.Value}
               Review Digest: {planReview.Envelope.ReviewDigest.Value}
               Approval URL: {approvalUrl}
               Expires at UTC: {expiresAtUtc:O}

               Open the approval URL in a browser, sign in with the same identity, and review the Gateway-rendered plan.
               You MUST call wait_for_plan_approval(planId="{planReview.Envelope.Id}") to poll for approval status (55 s timeout, repeat as needed). Do NOT wait for the user to confirm approval.
               When the status is Approved, call execute_approved_plan again to apply the plan.
               """;
    }
}
