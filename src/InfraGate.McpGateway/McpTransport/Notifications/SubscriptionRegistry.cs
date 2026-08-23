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

        foreach ((string planId, ConcurrentDictionary<string, byte> planSubscribers) in planSubscriptions)
        {
            planSubscribers.TryRemove(registrationId, out _);
            RemoveIfEmpty(planId, planSubscribers);
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
            RemoveIfEmpty(planId, planSubscribers);
        }
    }

    // Conditional removal: only deletes the planId entry if it still points at the exact
    // subscriber set instance we just emptied. Guards against removing an entry that
    // SubscribeToPlan's GetOrAdd has since resolved to a different instance for the same planId.
    private void RemoveIfEmpty(string planId, ConcurrentDictionary<string, byte> planSubscribers)
    {
        if (!planSubscribers.IsEmpty)
        {
            return;
        }

        ((ICollection<KeyValuePair<string, ConcurrentDictionary<string, byte>>>)planSubscriptions)
            .Remove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(planId, planSubscribers));
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
