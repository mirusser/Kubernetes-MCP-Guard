using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Notifications;

internal sealed class ApprovalNotificationDispatcher(ISubscriptionRegistry registry) : IApprovalNotificationDispatcher
{
    public async Task NotifyPlanApprovedAsync(string planId, CancellationToken ct)
    {
        IReadOnlyList<ISessionNotifier> sessions = registry.GetSessionsForPlan(planId);
        if (sessions.Count == 0)
        {
            return;
        }

        string resourceUri = NotificationsConventions.Resources.PlanStatusUri(planId);
        var @params = new ResourceUpdatedNotificationParams { Uri = resourceUri };

        await Task.WhenAll(sessions.Select(async session =>
        {
            await session.SendNotificationAsync(
                NotificationsConventions.Methods.ResourcesUpdated,
                @params,
                ct).ConfigureAwait(false);

            if (session.SessionId is not null)
            {
                registry.UnsubscribeFromPlan(session.SessionId, planId);
            }
        })).ConfigureAwait(false);
    }
}
