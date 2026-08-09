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
        var notifier = new FakeSubscriptionNotifier("listen-1");
        var registry = new FakeSubscriptionRegistry([notifier]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-1", CancellationToken.None);

        Assert.Single(notifier.SentMethods);
        Assert.Equal(NotificationsConventions.Methods.ResourcesUpdated, notifier.SentMethods[0]);
    }

    [Fact]
    public async Task NotifyPlanApprovedAsync_OneSubscriber_NotifiesWithCorrectPlanUri()
    {
        var notifier = new FakeSubscriptionNotifier("listen-1");
        var registry = new FakeSubscriptionRegistry([notifier]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-abc", CancellationToken.None);

        Assert.Single(notifier.SentUris);
        Assert.Equal(NotificationsConventions.Resources.PlanStatusUri("plan-abc"), notifier.SentUris[0]);
    }

    [Fact]
    public async Task NotifyPlanApprovedAsync_OneSubscriber_UnsubscribesAfterSend()
    {
        var notifier = new FakeSubscriptionNotifier("listen-1");
        var registry = new FakeSubscriptionRegistry([notifier]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-1", CancellationToken.None);

        Assert.Single(registry.Unsubscribes);
        Assert.Equal(("listen-1", "plan-1"), registry.Unsubscribes[0]);
    }

    [Fact]
    public async Task NotifyPlanApprovedAsync_MultipleSubscribers_NotifiesAll()
    {
        var notifier1 = new FakeSubscriptionNotifier("listen-1");
        var notifier2 = new FakeSubscriptionNotifier("listen-2");
        var registry = new FakeSubscriptionRegistry([notifier1, notifier2]);
        var dispatcher = new ApprovalNotificationDispatcher(registry);

        await dispatcher.NotifyPlanApprovedAsync("plan-1", CancellationToken.None);

        Assert.Single(notifier1.SentMethods);
        Assert.Single(notifier2.SentMethods);
        Assert.Equal(2, registry.Unsubscribes.Count);
    }

    private sealed class FakeSubscriptionNotifier(string registrationId) : ISubscriptionNotifier
    {
        private readonly List<string> sentMethods = [];
        private readonly List<string> sentUris = [];

        public string RegistrationId => registrationId;
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

    private sealed class FakeSubscriptionRegistry(IReadOnlyList<ISubscriptionNotifier> subscribers) : ISubscriptionRegistry
    {
        private readonly List<(string RegistrationId, string PlanId)> unsubscribes = [];

        public IReadOnlyList<(string RegistrationId, string PlanId)> Unsubscribes => unsubscribes;

        public IReadOnlyList<ISubscriptionNotifier> GetSubscribersForPlan(string planId) => subscribers;

        public void UnsubscribeFromPlan(string registrationId, string planId) =>
            unsubscribes.Add((registrationId, planId));

        public void RegisterSubscriber(string registrationId, ISubscriptionNotifier notifier) { }
        public void RemoveSubscriber(string registrationId) { }
        public void SubscribeToPlan(string registrationId, string planId) { }
    }
}
