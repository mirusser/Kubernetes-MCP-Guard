using System.Collections.Concurrent;

namespace InfraGate.McpGateway.Notifications;

internal sealed class SubscriptionRegistry : ISubscriptionRegistry
{
    // subscriptions/listen registration id → notifier
    private readonly ConcurrentDictionary<string, ISubscriptionNotifier> subscribers = new(StringComparer.Ordinal);

    // planId → set of subscriptions/listen registration ids
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> planSubscriptions = new(StringComparer.Ordinal);

    public void RegisterSubscriber(string registrationId, ISubscriptionNotifier notifier) =>
        subscribers[registrationId] = notifier;

    public void RemoveSubscriber(string registrationId)
    {
        subscribers.TryRemove(registrationId, out _);

        foreach ((string _, ConcurrentDictionary<string, byte>? planSubscribers) in planSubscriptions)
        {
            planSubscribers.TryRemove(registrationId, out _);
        }
    }

    public void SubscribeToPlan(string registrationId, string planId)
    {
        if (!subscribers.ContainsKey(registrationId))
        {
            return;
        }

        ConcurrentDictionary<string, byte> planSubscribers = planSubscriptions.GetOrAdd(
            planId,
            _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        planSubscribers.TryAdd(registrationId, 0);
    }

    public void UnsubscribeFromPlan(string registrationId, string planId)
    {
        if (planSubscriptions.TryGetValue(planId, out ConcurrentDictionary<string, byte>? planSubscribers))
        {
            planSubscribers.TryRemove(registrationId, out _);
        }
    }

    public IReadOnlyList<ISubscriptionNotifier> GetSubscribersForPlan(string planId)
    {
        if (!planSubscriptions.TryGetValue(planId, out ConcurrentDictionary<string, byte>? planSubscribers))
        {
            return [];
        }

        var result = new List<ISubscriptionNotifier>();
        foreach ((string? registrationId, byte _) in planSubscribers)
        {
            if (subscribers.TryGetValue(registrationId, out ISubscriptionNotifier? notifier))
            {
                result.Add(notifier);
            }
        }

        return result;
    }
}
