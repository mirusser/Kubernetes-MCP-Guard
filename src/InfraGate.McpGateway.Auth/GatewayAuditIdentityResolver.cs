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

        if (IsStaticBearerIdentity(user))
        {
            return StaticBearerIdentity();
        }

        return OAuthIdentity(user);
    }

    private static GatewayAuditIdentity Unauthenticated() => new(null, null);

    private static bool IsStaticBearerIdentity(ClaimsPrincipal user) =>
        string.Equals(user.Identity?.AuthenticationType, GatewayAuthConventions.Schemes.StaticBearer, StringComparison.Ordinal);

    private static GatewayAuditIdentity StaticBearerIdentity() => new(
        GatewayAuthConventions.Audit.LocalBearerSubject,
        GatewayAuthConventions.Audit.StaticBearerAuthenticationType);

    private static GatewayAuditIdentity OAuthIdentity(ClaimsPrincipal user)
    {
        var subject = ClaimValue(GatewayAuthConventions.Claims.PreferredUsername) ??
                      ClaimValue(GatewayAuthConventions.Claims.Email) ??
                      ClaimValue(GatewayAuthConventions.Claims.Subject) ??
                      ClaimValue(GatewayAuthConventions.Claims.ClientId);

        return new GatewayAuditIdentity(subject, GatewayAuthConventions.Audit.OAuthAuthenticationType);

        string? ClaimValue(string claimType)
        {
            return user.FindFirst(claimType)?.Value;
        }
    }
}
