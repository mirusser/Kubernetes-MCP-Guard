using InfraGate.McpGateway.Notifications;

namespace InfraGate.McpGateway.Tests.UnitTests.Notifications;

public sealed class SubscriptionRegistryTests
{
    [Fact]
    public void GetSubscribersForPlan_NothingSubscribed_ReturnsEmpty()
    {
        var registry = new SubscriptionRegistry();

        var result = registry.GetSubscribersForPlan("plan-1");

        Assert.Empty(result);
    }

    [Fact]
    public void SubscribeToPlan_RegisteredSubscriber_AppearsInGetSubscribersForPlan()
    {
        var registry = new SubscriptionRegistry();
        var notifier = new FakeSubscriptionNotifier("listen-1");
        registry.RegisterSubscriber("listen-1", notifier);
        registry.SubscribeToPlan("listen-1", "plan-1");

        var result = registry.GetSubscribersForPlan("plan-1");

        Assert.Single(result);
        Assert.Same(notifier, result[0]);
    }

    [Fact]
    public void SubscribeToPlan_UnknownRegistrationId_IsIgnored()
    {
        var registry = new SubscriptionRegistry();

        registry.SubscribeToPlan("unknown-listen", "plan-1");

        Assert.Empty(registry.GetSubscribersForPlan("plan-1"));
    }

    [Fact]
    public void UnsubscribeFromPlan_RemovesSubscriberFromPlan()
    {
        var registry = new SubscriptionRegistry();
        var notifier = new FakeSubscriptionNotifier("listen-1");
        registry.RegisterSubscriber("listen-1", notifier);
        registry.SubscribeToPlan("listen-1", "plan-1");

        registry.UnsubscribeFromPlan("listen-1", "plan-1");

        Assert.Empty(registry.GetSubscribersForPlan("plan-1"));
    }

    [Fact]
    public void RemoveSubscriber_ClearsAllSubscriptionsForRegistration()
    {
        var registry = new SubscriptionRegistry();
        var notifier = new FakeSubscriptionNotifier("listen-1");
        registry.RegisterSubscriber("listen-1", notifier);
        registry.SubscribeToPlan("listen-1", "plan-1");
        registry.SubscribeToPlan("listen-1", "plan-2");

        registry.RemoveSubscriber("listen-1");

        Assert.Empty(registry.GetSubscribersForPlan("plan-1"));
        Assert.Empty(registry.GetSubscribersForPlan("plan-2"));
    }

    [Fact]
    public void GetSubscribersForPlan_MultipleSubscribers_ReturnsAll()
    {
        var registry = new SubscriptionRegistry();
        var notifier1 = new FakeSubscriptionNotifier("listen-1");
        var notifier2 = new FakeSubscriptionNotifier("listen-2");
        registry.RegisterSubscriber("listen-1", notifier1);
        registry.RegisterSubscriber("listen-2", notifier2);
        registry.SubscribeToPlan("listen-1", "plan-1");
        registry.SubscribeToPlan("listen-2", "plan-1");

        var result = registry.GetSubscribersForPlan("plan-1");

        Assert.Equal(2, result.Count);
        Assert.Contains(notifier1, result);
        Assert.Contains(notifier2, result);
    }

    [Fact]
    public void RemoveSubscriber_DoesNotAffectOtherSubscribers()
    {
        var registry = new SubscriptionRegistry();
        var notifier1 = new FakeSubscriptionNotifier("listen-1");
        var notifier2 = new FakeSubscriptionNotifier("listen-2");
        registry.RegisterSubscriber("listen-1", notifier1);
        registry.RegisterSubscriber("listen-2", notifier2);
        registry.SubscribeToPlan("listen-1", "plan-1");
        registry.SubscribeToPlan("listen-2", "plan-1");

        registry.RemoveSubscriber("listen-1");

        var result = registry.GetSubscribersForPlan("plan-1");
        Assert.Single(result);
        Assert.Same(notifier2, result[0]);
    }

    [Fact]
    public void SubscribeToPlan_DuplicateSubscription_OnlyAppearsOnce()
    {
        var registry = new SubscriptionRegistry();
        var notifier = new FakeSubscriptionNotifier("listen-1");
        registry.RegisterSubscriber("listen-1", notifier);
        registry.SubscribeToPlan("listen-1", "plan-1");
        registry.SubscribeToPlan("listen-1", "plan-1");

        var result = registry.GetSubscribersForPlan("plan-1");

        Assert.Single(result);
    }

    private sealed class FakeSubscriptionNotifier(string registrationId) : ISubscriptionNotifier
    {
        public string RegistrationId => registrationId;

        public Task SendNotificationAsync<TParams>(string method, TParams @params, CancellationToken ct)
            where TParams : notnull =>
            Task.CompletedTask;
    }
}
