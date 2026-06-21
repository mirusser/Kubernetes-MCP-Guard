using System.Globalization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace InfraGate.McpGateway.Auth;

internal static class TokenClaimDates
{
    public static bool TryGetUnixTimeClaim(JsonWebToken token, string claimType, out DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimType);

        var claim = token.Claims.FirstOrDefault(candidate =>
            string.Equals(candidate.Type, claimType, StringComparison.Ordinal));
        if (claim is null ||
            !long.TryParse(claim.Value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
        {
            value = default;
            return false;
        }

        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }

        return true;
    }
}
