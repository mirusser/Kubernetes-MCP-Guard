using System.Collections.Concurrent;

namespace InfraGate.McpGateway.Notifications;

internal sealed class SubscriptionRegistry : ISubscriptionRegistry
{
    // sessionId → notifier
    private readonly ConcurrentDictionary<string, ISessionNotifier> sessions = new();

    // planId → set of sessionIds
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> planSubscriptions = new();

    public void RegisterSession(string sessionId, ISessionNotifier notifier) =>
        sessions[sessionId] = notifier;

    public void RemoveSession(string sessionId)
    {
        sessions.TryRemove(sessionId, out _);

        foreach (var (_, subscribers) in planSubscriptions)
        {
            subscribers.TryRemove(sessionId, out _);
        }
    }

    public void BindSubject(string sessionId, string requesterSubject)
    {
        // Subject binding is stored implicitly via the session; reserved for future routing.
    }

    public void SubscribeToPlan(string sessionId, string planId)
    {
        if (!sessions.ContainsKey(sessionId))
        {
            return;
        }

        var subscribers = planSubscriptions.GetOrAdd(planId, _ => new ConcurrentDictionary<string, byte>());
        subscribers.TryAdd(sessionId, 0);
    }

    public void UnsubscribeFromPlan(string sessionId, string planId)
    {
        if (planSubscriptions.TryGetValue(planId, out var subscribers))
        {
            subscribers.TryRemove(sessionId, out _);
        }
    }

    public IReadOnlyList<ISessionNotifier> GetSessionsForPlan(string planId)
    {
        if (!planSubscriptions.TryGetValue(planId, out var subscribers))
        {
            return [];
        }

        var result = new List<ISessionNotifier>();
        foreach (var (sessionId, _) in subscribers)
        {
            if (sessions.TryGetValue(sessionId, out var notifier))
            {
                result.Add(notifier);
            }
        }

        return result;
    }
}
