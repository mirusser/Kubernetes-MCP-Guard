using System.Globalization;
using System.Text;
using InfraGate.McpGateway.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class TokenClaimDatesTests
{
    [Fact]
    public void TryGetUnixTimeClaim_OutOfRangeClaim_ReturnsFalse()
    {
        var token = new JsonWebToken(CreateUnsignedJwtWithClaim(
            GatewayAuthConventions.Claims.IssuedAt,
            long.MaxValue.ToString(CultureInfo.InvariantCulture)));

        bool result = TokenClaimDates.TryGetUnixTimeClaim(
            token,
            GatewayAuthConventions.Claims.IssuedAt,
            out DateTimeOffset value);

        Assert.False(result);
        Assert.Equal(default, value);
    }

    private static string CreateUnsignedJwtWithClaim(string claimType, string claimValue)
    {
        string header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
        string payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(
            $"{{\"{claimType}\":{claimValue}}}"));
        return $"{header}.{payload}.";
    }
}
