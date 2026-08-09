using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpGateway.Notifications;

internal sealed class PlanStatusSubscriptionsListenHandler(ISubscriptionRegistry subscriptionRegistry)
{
    public async ValueTask<EmptyResult> ListenAsync(
        RequestContext<SubscriptionsListenRequestParams> request,
        CancellationToken cancellationToken)
    {
        RequestId subscriptionId = request.JsonRpcRequest.Id;
        string registrationId = CreateRegistrationId(subscriptionId);
        IReadOnlyList<PlanSubscription> grantedSubscriptions = GetGrantedSubscriptions(
            request.Params.Notifications.ResourceSubscriptions);

        await SendAcknowledgementAsync(request, subscriptionId, grantedSubscriptions, cancellationToken)
            .ConfigureAwait(false);

        subscriptionRegistry.RegisterSubscriber(
            registrationId,
            new McpSubscriptionsListenNotifier(request.Server, registrationId, subscriptionId));
        foreach (PlanSubscription subscription in grantedSubscriptions)
        {
            subscriptionRegistry.SubscribeToPlan(registrationId, subscription.PlanId);
        }

        try
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                cancelled);
            await cancelled.Task.ConfigureAwait(false);
        }
        finally
        {
            subscriptionRegistry.RemoveSubscriber(registrationId);
        }

        return new EmptyResult();
    }

    private static IReadOnlyList<PlanSubscription> GetGrantedSubscriptions(IList<string>? requestedUris)
    {
        if (requestedUris is null)
        {
            return [];
        }

        var granted = new List<PlanSubscription>();
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (string uri in requestedUris)
        {
            if (seenUris.Add(uri) &&
                PlanStatusResourceHandler.TryParsePlanStatusUri(uri, out string? planId) &&
                planId is not null)
            {
                granted.Add(new PlanSubscription(uri, planId));
            }
        }

        return granted;
    }

    private static async Task SendAcknowledgementAsync(
        RequestContext<SubscriptionsListenRequestParams> request,
        RequestId subscriptionId,
        IReadOnlyList<PlanSubscription> grantedSubscriptions,
        CancellationToken cancellationToken)
    {
        var acknowledgement = new JsonRpcNotification
        {
            Method = NotificationMethods.SubscriptionsAcknowledgedNotification,
            Params = JsonSerializer.SerializeToNode(
                new SubscriptionsAcknowledgedNotificationParams
                {
                    Notifications = new SubscriptionsListenNotifications
                    {
                        ResourceSubscriptions = grantedSubscriptions.Select(subscription => subscription.Uri).ToList()
                    }
                },
                McpJsonUtilities.DefaultOptions)
        };
        TagWithSubscriptionId(acknowledgement, subscriptionId);
        await request.Server.SendMessageAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
    }

    private static void TagWithSubscriptionId(JsonRpcNotification notification, RequestId subscriptionId)
    {
        var paramsObject = notification.Params as JsonObject ?? new JsonObject();
        var meta = paramsObject["_meta"] as JsonObject ?? new JsonObject();
        meta[MetaKeys.SubscriptionId] = subscriptionId.Id switch
        {
            string stringId => JsonValue.Create(stringId),
            long longId => JsonValue.Create(longId),
            _ => null
        };
        paramsObject["_meta"] = meta;
        notification.Params = paramsObject;
    }

    private static string CreateRegistrationId(RequestId subscriptionId) =>
        subscriptionId.Id switch
        {
            string or long => Guid.NewGuid().ToString("N"),
            _ => throw new McpException("The subscriptions/listen request is missing its JSON-RPC request id.")
        };

    private sealed record class PlanSubscription(string Uri, string PlanId);
}
