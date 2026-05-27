using InfraGate.ClientCredentials;
using InfraGate.DownstreamAuth;

namespace InfraGate.McpGateway.DownstreamAuth;

internal sealed class ClientCredentialsDownstreamServiceTokenProvider(
    IClientCredentialsTokenProvider inner) : IDownstreamServiceTokenProvider
{

    public async Task<string> GetServiceTokenAsync(CancellationToken cancellationToken)
    {
        string token = await inner.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        return DownstreamAuthConventions.BearerPrefix + token;
    }

    public async Task<string> RefreshServiceTokenAsync(CancellationToken cancellationToken)
    {
        string token = await inner.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
        return DownstreamAuthConventions.BearerPrefix + token;
    }
}
