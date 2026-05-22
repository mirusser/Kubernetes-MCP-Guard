using InfraGate.McpGateway.Notifications;

namespace InfraGate.McpGateway.Tests.UnitTests.Notifications;

public sealed class ApprovalNotificationDispatcherTests
{
    [Fact]
    public async Task NotifyPlanApprovedAsync_NoSubscribers_CompletesWithoutError()
    {
        var registry = new FakeSubscriptionRegistry([]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-1", CancellationToken.None);

        Assert.Empty(registry.Unsubscribes);
    }

    [Fact]
    public async Task NotifyPlanApprovedAsync_OneSubscriber_SendsResourceUpdatedNotification()
    {
        var notifier = new FakeSessionNotifier("session-1");
        var registry = new FakeSubscriptionRegistry([notifier]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-1", CancellationToken.None);

        Assert.Single(notifier.SentMethods);
        Assert.Equal(NotificationsConventions.Methods.ResourcesUpdated, notifier.SentMethods[0]);
    }

    [Fact]
    public async Task NotifyPlanApprovedAsync_OneSubscriber_NotifiesWithCorrectPlanUri()
    {
        var notifier = new FakeSessionNotifier("session-1");
        var registry = new FakeSubscriptionRegistry([notifier]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-abc", CancellationToken.None);

        Assert.Single(notifier.SentUris);
        Assert.Equal(NotificationsConventions.Resources.PlanStatusUri("plan-abc"), notifier.SentUris[0]);
    }

    [Fact]
    public async Task NotifyPlanApprovedAsync_OneSubscriber_UnsubscribesAfterSend()
    {
        var notifier = new FakeSessionNotifier("session-1");
        var registry = new FakeSubscriptionRegistry([notifier]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-1", CancellationToken.None);

        Assert.Single(registry.Unsubscribes);
        Assert.Equal(("session-1", "plan-1"), registry.Unsubscribes[0]);
    }

    [Fact]
    public async Task NotifyPlanApprovedAsync_MultipleSubscribers_NotifiesAll()
    {
        var notifier1 = new FakeSessionNotifier("session-1");
        var notifier2 = new FakeSessionNotifier("session-2");
        var registry = new FakeSubscriptionRegistry([notifier1, notifier2]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-1", CancellationToken.None);

        Assert.Single(notifier1.SentMethods);
        Assert.Single(notifier2.SentMethods);
        Assert.Equal(2, registry.Unsubscribes.Count);
    }

    [Fact]
    public async Task NotifyPlanApprovedAsync_SubscriberWithNullSessionId_IsSkipped()
    {
        var notifierWithNull = new FakeSessionNotifier(null);
        var registry = new FakeSubscriptionRegistry([notifierWithNull]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-1", CancellationToken.None);

        Assert.Single(notifierWithNull.SentMethods);
        Assert.Empty(registry.Unsubscribes);
    }

    private sealed class FakeSessionNotifier(string? sessionId) : ISessionNotifier
    {
        private readonly List<string> sentMethods = [];
        private readonly List<string> sentUris = [];

        public string? SessionId => sessionId;
        public IReadOnlyList<string> SentMethods => sentMethods;
        public IReadOnlyList<string> SentUris => sentUris;

        public Task SendNotificationAsync<TParams>(string method, TParams @params, CancellationToken ct)
            where TParams : notnull
        {
            sentMethods.Add(method);
            if (@params is ModelContextProtocol.Protocol.ResourceUpdatedNotificationParams p)
            {
                sentUris.Add(p.Uri);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSubscriptionRegistry(IReadOnlyList<ISessionNotifier> sessions) : ISubscriptionRegistry
    {
        private readonly List<(string SessionId, string PlanId)> unsubscribes = [];

        public IReadOnlyList<(string SessionId, string PlanId)> Unsubscribes => unsubscribes;

        public IReadOnlyList<ISessionNotifier> GetSessionsForPlan(string planId) => sessions;

        public void UnsubscribeFromPlan(string sessionId, string planId) =>
            unsubscribes.Add((sessionId, planId));

        public void RegisterSession(string sessionId, ISessionNotifier notifier) { }
        public void RemoveSession(string sessionId) { }
        public void BindSubject(string sessionId, string requesterSubject) { }
        public void SubscribeToPlan(string sessionId, string planId) { }
    }
}
