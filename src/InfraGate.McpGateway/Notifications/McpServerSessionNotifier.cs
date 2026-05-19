using ModelContextProtocol.Server;

namespace InfraGate.McpGateway.Notifications;

// Wraps McpServer at the seam so ApprovalNotificationDispatcher stays testable
// without a mocking framework.
internal sealed class McpServerSessionNotifier(McpServer server) : ISessionNotifier
{
    public string? SessionId => server.SessionId;

    public Task SendNotificationAsync<TParams>(string method, TParams @params, CancellationToken ct)
        where TParams : notnull =>
        server.SendNotificationAsync(method, @params, System.Text.Json.JsonSerializerOptions.Default, ct);
}
