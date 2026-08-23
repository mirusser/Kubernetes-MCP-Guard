using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway.Notifications;

internal sealed class ApprovalNotificationDispatcher(ISubscriptionRegistry registry) : IApprovalNotificationDispatcher
{
    public async Task NotifyPlanApprovedAsync(string planId, CancellationToken ct)
    {
        IReadOnlyList<ISubscriptionNotifier> subscribers = registry.GetSubscribersForPlan(planId);
        if (subscribers.Count == 0)
        {
            return;
        }

        string resourceUri = NotificationsConventions.Resources.PlanStatusUri(planId);
        var @params = new ResourceUpdatedNotificationParams { Uri = resourceUri };

        await Task.WhenAll(subscribers.Select(async subscriber =>
        {
            await subscriber.SendNotificationAsync(
                NotificationsConventions.Methods.ResourcesUpdated,
                @params,
                ct).ConfigureAwait(false);

            registry.UnsubscribeFromPlan(subscriber.RegistrationId, planId);
        })).ConfigureAwait(false);
    }
}
