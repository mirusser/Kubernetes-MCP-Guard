using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Notifications;

internal static class NotificationsConventions
{
    // Key for storing the MCP session ID on HttpContext.Items per request.
    internal static readonly object McpSessionIdItemKey = new();

    internal static class Resources
    {
        internal const string PlanStatusScheme = "plan";
        internal const string PlanStatusMimeType = "application/json";
        internal const string PlanStatusTemplateName = "approval-plan-status";
        internal const string PlanStatusUriTemplate = "plan://{planId}/status";
        internal const string PlanStatusUriPrefix = "plan://";
        internal const string PlanStatusUriSuffix = "/status";

        internal static string PlanStatusUri(string planId) =>
            $"{PlanStatusUriPrefix}{planId}{PlanStatusUriSuffix}";
    }

    internal static class Methods
    {
        internal const string ResourcesUpdated = NotificationMethods.ResourceUpdatedNotification;
    }
}
