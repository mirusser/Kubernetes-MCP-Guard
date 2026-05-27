namespace InfraGate.McpGateway.Notifications;

internal interface ISessionNotifier
{
    string? SessionId { get; }
    Task SendNotificationAsync<TParams>(string method, TParams @params, CancellationToken ct)
        where TParams : notnull;
}
