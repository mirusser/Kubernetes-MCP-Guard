namespace InfraGate.McpGateway.Notifications;

internal static class NotificationsConventions
{
    // Key for storing the MCP session ID on HttpContext.Items per request.
    internal static readonly object McpSessionIdItemKey = new();

    internal static class Resources
    {
        internal const string PlanStatusScheme = "plan";
        internal const string PlanStatusMimeType = "application/json";

        internal static string PlanStatusUri(string planId) =>
            $"{PlanStatusScheme}://{planId}/status";
    }

    internal static class Methods
    {
        internal const string ResourcesUpdated = "notifications/resources/updated";
    }
}
