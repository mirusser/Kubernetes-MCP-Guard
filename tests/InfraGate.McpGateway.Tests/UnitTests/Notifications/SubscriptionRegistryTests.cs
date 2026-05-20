using InfraGate.McpGateway.Notifications;

namespace InfraGate.McpGateway.Tests.UnitTests.Notifications;

public sealed class SubscriptionRegistryTests
{
    [Fact]
    public void GetSessionsForPlan_NothingSubscribed_ReturnsEmpty()
    {
        var registry = new SubscriptionRegistry();

        var result = registry.GetSessionsForPlan("plan-1");

        Assert.Empty(result);
    }

    [Fact]
    public void SubscribeToPlan_RegisteredSession_AppearsInGetSessionsForPlan()
    {
        var registry = new SubscriptionRegistry();
        var notifier = new FakeSessionNotifier("session-1");
        registry.RegisterSession("session-1", notifier);
        registry.SubscribeToPlan("session-1", "plan-1");

        var result = registry.GetSessionsForPlan("plan-1");

        Assert.Single(result);
        Assert.Same(notifier, result[0]);
    }

    [Fact]
    public void SubscribeToPlan_UnknownSessionId_IsIgnored()
    {
        var registry = new SubscriptionRegistry();

        registry.SubscribeToPlan("unknown-session", "plan-1");

        Assert.Empty(registry.GetSessionsForPlan("plan-1"));
    }

    [Fact]
    public void UnsubscribeFromPlan_RemovesSessionFromPlan()
    {
        var registry = new SubscriptionRegistry();
        var notifier = new FakeSessionNotifier("session-1");
        registry.RegisterSession("session-1", notifier);
        registry.SubscribeToPlan("session-1", "plan-1");

        registry.UnsubscribeFromPlan("session-1", "plan-1");

        Assert.Empty(registry.GetSessionsForPlan("plan-1"));
    }

    [Fact]
    public void RemoveSession_ClearsAllSubscriptionsForSession()
    {
        var registry = new SubscriptionRegistry();
        var notifier = new FakeSessionNotifier("session-1");
        registry.RegisterSession("session-1", notifier);
        registry.SubscribeToPlan("session-1", "plan-1");
        registry.SubscribeToPlan("session-1", "plan-2");

        registry.RemoveSession("session-1");

        Assert.Empty(registry.GetSessionsForPlan("plan-1"));
        Assert.Empty(registry.GetSessionsForPlan("plan-2"));
    }

    [Fact]
    public void GetSessionsForPlan_MultipleSessions_ReturnsAll()
    {
        var registry = new SubscriptionRegistry();
        var notifier1 = new FakeSessionNotifier("session-1");
        var notifier2 = new FakeSessionNotifier("session-2");
        registry.RegisterSession("session-1", notifier1);
        registry.RegisterSession("session-2", notifier2);
        registry.SubscribeToPlan("session-1", "plan-1");
        registry.SubscribeToPlan("session-2", "plan-1");

        var result = registry.GetSessionsForPlan("plan-1");

        Assert.Equal(2, result.Count);
        Assert.Contains(notifier1, result);
        Assert.Contains(notifier2, result);
    }

    [Fact]
    public void RemoveSession_DoesNotAffectOtherSessions()
    {
        var registry = new SubscriptionRegistry();
        var notifier1 = new FakeSessionNotifier("session-1");
        var notifier2 = new FakeSessionNotifier("session-2");
        registry.RegisterSession("session-1", notifier1);
        registry.RegisterSession("session-2", notifier2);
        registry.SubscribeToPlan("session-1", "plan-1");
        registry.SubscribeToPlan("session-2", "plan-1");

        registry.RemoveSession("session-1");

        var result = registry.GetSessionsForPlan("plan-1");
        Assert.Single(result);
        Assert.Same(notifier2, result[0]);
    }

    [Fact]
    public void BindSubject_ThenSubscribeToPlan_AssociatesCorrectly()
    {
        var registry = new SubscriptionRegistry();
        var notifier = new FakeSessionNotifier("session-1");
        registry.RegisterSession("session-1", notifier);
        registry.BindSubject("session-1", "user@example.com");
        registry.SubscribeToPlan("session-1", "plan-1");

        var result = registry.GetSessionsForPlan("plan-1");

        Assert.Single(result);
        Assert.Same(notifier, result[0]);
    }

    [Fact]
    public void SubscribeToPlan_DuplicateSubscription_OnlyAppearsOnce()
    {
        var registry = new SubscriptionRegistry();
        var notifier = new FakeSessionNotifier("session-1");
        registry.RegisterSession("session-1", notifier);
        registry.SubscribeToPlan("session-1", "plan-1");
        registry.SubscribeToPlan("session-1", "plan-1");

        var result = registry.GetSessionsForPlan("plan-1");

        Assert.Single(result);
    }

    private sealed class FakeSessionNotifier(string sessionId) : ISessionNotifier
    {
        public string? SessionId => sessionId;

        public Task SendNotificationAsync<TParams>(string method, TParams @params, CancellationToken ct)
            where TParams : notnull =>
            Task.CompletedTask;
    }
}
