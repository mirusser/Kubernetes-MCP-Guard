namespace InfraGate.McpGateway.Notifications;

internal interface ISubscriptionRegistry
{
    void RegisterSubscriber(string registrationId, ISubscriptionNotifier notifier);
    void RemoveSubscriber(string registrationId);
    void SubscribeToPlan(string registrationId, string planId);
    void UnsubscribeFromPlan(string registrationId, string planId);
    IReadOnlyList<ISubscriptionNotifier> GetSubscribersForPlan(string planId);
}
