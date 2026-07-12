using Microsoft.IdentityModel.JsonWebTokens;

namespace InfraGate.McpGateway.Auth;

internal interface ITokenIntrospectionClient
{
    Task<TokenIntrospectionResult> IntrospectAsync(
        JsonWebToken accessToken,
        CancellationToken cancellationToken);
}
