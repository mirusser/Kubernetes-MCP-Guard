using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfraGate.McpGateway.Notifications;

internal sealed class McpSubscriptionsListenNotifier(
    McpServer server,
    string registrationId,
    RequestId subscriptionId) : ISubscriptionNotifier
{
    public string RegistrationId => registrationId;

    public Task SendNotificationAsync<TParams>(string method, TParams @params, CancellationToken ct)
        where TParams : notnull
    {
        var paramsObject = JsonSerializer.SerializeToNode(@params, McpJsonUtilities.DefaultOptions) as JsonObject
            ?? new JsonObject();
        var meta = paramsObject["_meta"] as JsonObject ?? new JsonObject();
        meta[MetaKeys.SubscriptionId] = CreateSubscriptionIdNode(subscriptionId);
        paramsObject["_meta"] = meta;

        return server.SendMessageAsync(
            new JsonRpcNotification
            {
                Method = method,
                Params = paramsObject
            },
            ct);
    }

    private static JsonNode? CreateSubscriptionIdNode(RequestId requestId) =>
        requestId.Id switch
        {
            string stringId => JsonValue.Create(stringId),
            long longId => JsonValue.Create(longId),
            _ => null
        };
}
