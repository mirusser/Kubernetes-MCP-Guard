using System.Security.Claims;

namespace InfraGate.McpGateway.Auth;

public static class GatewayAuditIdentityResolver
{
    private static readonly IReadOnlySet<string> KnownServiceClients =
        new HashSet<string>(StringComparer.Ordinal)
        {
            GatewayAuthConventions.ServiceClients.ObserverClientId
        };

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

        string identityKind = GatewayAuthConventions.Audit.HumanIdentityKind;

        // Check azp (authorized party) claim — identifies the OAuth client that requested the token.
        // Service clients (machine identities) are resolved to a distinguished identity kind.
        string? azp = ClaimValue(GatewayAuthConventions.Claims.AuthorizedParty);
        if (azp is not null && KnownServiceClients.Contains(azp))
        {
            identityKind = GatewayAuthConventions.Audit.ServiceIdentityKind;
        }

        return new GatewayAuditIdentity(subject, GatewayAuthConventions.Audit.OAuthAuthenticationType, identityKind);

        string? ClaimValue(string claimType)
        {
            return user.FindFirst(claimType)?.Value;
        }
    }
}
