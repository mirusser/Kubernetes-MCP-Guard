using System.Collections.Concurrent;

namespace InfraGate.McpGateway.Notifications;

internal sealed class SubscriptionRegistry : ISubscriptionRegistry
{
    // sessionId → notifier
    private readonly ConcurrentDictionary<string, ISessionNotifier> sessions = new(StringComparer.Ordinal);

    // planId → set of sessionIds
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> planSubscriptions = new(StringComparer.Ordinal);

    public void RegisterSession(string sessionId, ISessionNotifier notifier) =>
        sessions[sessionId] = notifier;

    public void RemoveSession(string sessionId)
    {
        sessions.TryRemove(sessionId, out _);

        foreach ((string _, ConcurrentDictionary<string, byte>? subscribers) in planSubscriptions)
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

        ConcurrentDictionary<string, byte> subscribers = planSubscriptions.GetOrAdd(planId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        subscribers.TryAdd(sessionId, 0);
    }

    public void UnsubscribeFromPlan(string sessionId, string planId)
    {
        if (planSubscriptions.TryGetValue(planId, out ConcurrentDictionary<string, byte>? subscribers))
        {
            subscribers.TryRemove(sessionId, out _);
        }
    }

    public IReadOnlyList<ISessionNotifier> GetSessionsForPlan(string planId)
    {
        if (!planSubscriptions.TryGetValue(planId, out ConcurrentDictionary<string, byte>? subscribers))
        {
            return [];
        }

        var result = new List<ISessionNotifier>();
        foreach ((string? sessionId, byte _) in subscribers)
        {
            if (sessions.TryGetValue(sessionId, out ISessionNotifier? notifier))
            {
                result.Add(notifier);
            }
        }

        return result;
    }
}
