namespace InfraGate.McpGateway.Notifications;

internal interface ISubscriptionNotifier
{
    string RegistrationId { get; }
    Task SendNotificationAsync<TParams>(string method, TParams @params, CancellationToken ct)
        where TParams : notnull;
}
