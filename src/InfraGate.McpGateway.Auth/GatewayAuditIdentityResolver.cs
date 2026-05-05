using System.Security.Claims;

namespace InfraGate.McpGateway.Auth;

public static class GatewayAuditIdentityResolver
{
    public static GatewayAuditIdentity Resolve(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated is not true)
        {
            return Unauthenticated();
        }

        return OAuthIdentity(user);
    }

    private static GatewayAuditIdentity Unauthenticated() => new(null, null);

    private static GatewayAuditIdentity OAuthIdentity(ClaimsPrincipal user)
    {
        var subject = ClaimValue(GatewayAuthConventions.Claims.Subject) ??
                      ClaimValue(GatewayAuthConventions.Claims.ClientId) ??
                      ClaimValue(GatewayAuthConventions.Claims.PreferredUsername) ??
                      ClaimValue(GatewayAuthConventions.Claims.Email);

        return new GatewayAuditIdentity(subject, GatewayAuthConventions.Audit.OAuthAuthenticationType);

        string? ClaimValue(string claimType)
        {
            return user.FindFirst(claimType)?.Value;
        }
    }
}
