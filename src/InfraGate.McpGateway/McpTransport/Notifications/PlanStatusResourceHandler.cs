using InfraGate.Approvals;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Notifications;

internal sealed class PlanStatusResourceHandler(
    IApprovalPlanWorkflow approvalPlans,
    ISubscriptionRegistry subscriptionRegistry)
{
    public ListResourceTemplatesResult ListTemplates() => new()
    {
        ResourceTemplates =
        [
            new ResourceTemplate
            {
                Name = NotificationsConventions.Resources.PlanStatusTemplateName,
                UriTemplate = NotificationsConventions.Resources.PlanStatusUriTemplate,
                Description = "Current approval plan status by plan id.",
                MimeType = NotificationsConventions.Resources.PlanStatusMimeType
            }
        ]
    };

    public async Task<ReadResourceResult> ReadAsync(ReadResourceRequestParams request, CancellationToken ct)
    {
        string planId = ParsePlanStatusUri(request.Uri);
        var status = await approvalPlans.GetPlanStatusAsync(planId, ct).ConfigureAwait(false);
        string uri = NotificationsConventions.Resources.PlanStatusUri(planId);

        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = uri,
                    MimeType = NotificationsConventions.Resources.PlanStatusMimeType,
                    Text = PlanStatusResponse.Serialize(planId, status.Status)
                }
            ]
        };
    }

    public EmptyResult Subscribe(string? sessionId, SubscribeRequestParams request)
    {
        string planId = ParsePlanStatusUri(request.Uri);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            subscriptionRegistry.SubscribeToPlan(sessionId, planId);
        }

        return new EmptyResult();
    }

    public EmptyResult Unsubscribe(string? sessionId, UnsubscribeRequestParams request)
    {
        string planId = ParsePlanStatusUri(request.Uri);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            subscriptionRegistry.UnsubscribeFromPlan(sessionId, planId);
        }

        return new EmptyResult();
    }

    private static string ParsePlanStatusUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) ||
            !uri.StartsWith(NotificationsConventions.Resources.PlanStatusUriPrefix, StringComparison.Ordinal) ||
            !uri.EndsWith(NotificationsConventions.Resources.PlanStatusUriSuffix, StringComparison.Ordinal))
        {
            throw InvalidPlanStatusUri(uri);
        }

        int start = NotificationsConventions.Resources.PlanStatusUriPrefix.Length;
        int length = uri.Length - start - NotificationsConventions.Resources.PlanStatusUriSuffix.Length;
        if (length <= 0)
        {
            throw InvalidPlanStatusUri(uri);
        }

        string planId = uri.Substring(start, length);
        if (!IsSafePlanId(planId))
        {
            throw InvalidPlanStatusUri(uri);
        }

        return planId;
    }

    private static bool IsSafePlanId(string planId) =>
        planId.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');

    private static McpException InvalidPlanStatusUri(string? uri) =>
        new($"Unsupported plan status resource URI '{uri}'. Expected {NotificationsConventions.Resources.PlanStatusUriTemplate}.");
}
