namespace InfraGate.McpGateway.DownstreamAuth;

internal interface IDownstreamServiceTokenProvider
{
    // Returns "Bearer <token>", ready to attach to _meta
    Task<string> GetServiceTokenAsync(CancellationToken cancellationToken);

    // Force-refresh the cached token (called after 401 rejection from server)
    Task<string> RefreshServiceTokenAsync(CancellationToken cancellationToken);
}
