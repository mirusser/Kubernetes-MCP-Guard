namespace InfraGate.McpGateway.DownstreamAuth;

internal sealed class NullDownstreamServiceTokenProvider : IDownstreamServiceTokenProvider
{
    public NullDownstreamServiceTokenProvider() { }
    public Task<string> GetServiceTokenAsync(CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);

    public Task<string> RefreshServiceTokenAsync(CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);
}
