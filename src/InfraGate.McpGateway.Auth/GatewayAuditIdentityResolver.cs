using System.Security.Claims;

namespace InfraGate.McpGateway.Auth;

public sealed record GatewayAuditIdentity(string? Subject, string? AuthenticationType);

public static class GatewayAuditIdentityResolver
{
    public static GatewayAuditIdentity Resolve(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated is not true)
        {
            return new GatewayAuditIdentity(null, null);
        }

        if (string.Equals(user.Identity.AuthenticationType, GatewayAuthConventions.Schemes.StaticBearer, StringComparison.Ordinal))
        {
            return new GatewayAuditIdentity(
                GatewayAuthConventions.Audit.LocalBearerSubject,
                GatewayAuthConventions.Audit.StaticBearerAuthenticationType);
        }

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
