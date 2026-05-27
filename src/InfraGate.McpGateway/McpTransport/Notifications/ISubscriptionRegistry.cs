namespace InfraGate.McpGateway.Notifications;

internal interface ISubscriptionRegistry
{
    void RegisterSession(string sessionId, ISessionNotifier notifier);
    void RemoveSession(string sessionId);
    void BindSubject(string sessionId, string requesterSubject);
    void SubscribeToPlan(string sessionId, string planId);
    void UnsubscribeFromPlan(string sessionId, string planId);
    IReadOnlyList<ISessionNotifier> GetSessionsForPlan(string planId);
}
