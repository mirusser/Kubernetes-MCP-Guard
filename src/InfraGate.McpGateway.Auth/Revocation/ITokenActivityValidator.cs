using Microsoft.IdentityModel.JsonWebTokens;

namespace InfraGate.McpGateway.Auth;

internal interface ITokenActivityValidator
{
    Task<bool> IsActiveAsync(JsonWebToken accessToken, CancellationToken cancellationToken);
}
