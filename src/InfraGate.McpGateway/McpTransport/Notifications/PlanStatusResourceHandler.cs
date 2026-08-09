using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Notifications;

internal sealed class PlanStatusResourceHandler(
    IApprovalPlanWorkflow approvalPlans)
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
        PlanStatusResult status = await approvalPlans.GetPlanStatusAsync(planId, ct).ConfigureAwait(false);
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

    private static string ParsePlanStatusUri(string? uri)
    {
        if (!TryParsePlanStatusUri(uri, out string? planId) || planId is null)
        {
            throw InvalidPlanStatusUri(uri);
        }

        return planId;
    }

    internal static bool TryParsePlanStatusUri(string? uri, out string? planId)
    {
        planId = null;
        if (string.IsNullOrWhiteSpace(uri) ||
            !uri.StartsWith(NotificationsConventions.Resources.PlanStatusUriPrefix, StringComparison.Ordinal) ||
            !uri.EndsWith(NotificationsConventions.Resources.PlanStatusUriSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        int start = NotificationsConventions.Resources.PlanStatusUriPrefix.Length;
        int length = uri.Length - start - NotificationsConventions.Resources.PlanStatusUriSuffix.Length;
        if (length <= 0)
        {
            return false;
        }

        planId = uri[start..(start + length)];
        if (!IsSafePlanId(planId))
        {
            planId = null;
            return false;
        }

        return true;
    }

    private static bool IsSafePlanId(string planId) =>
        planId.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');

    private static McpException InvalidPlanStatusUri(string? uri) =>
        new($"Unsupported plan status resource URI '{uri}'. Expected {NotificationsConventions.Resources.PlanStatusUriTemplate}.");
}
