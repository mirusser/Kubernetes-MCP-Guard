using System.Security.Claims;

namespace InfraGate.McpGateway.Auth;

public static class GatewayAuditIdentityResolver
{
    private static readonly IReadOnlyDictionary<string, string> KnownServiceClients =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GatewayAuthConventions.ServiceClients.ObserverClientId] =
                FormatServiceSubject(GatewayAuthConventions.ServiceClients.ObserverClientId),
            [GatewayAuthConventions.ServiceClients.PlannerClientId] = "service:planner",
            [GatewayAuthConventions.ServiceClients.ExecutorClientId] = "service:executor"
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
        string? subject;
        string identityKind = GatewayAuthConventions.Audit.HumanIdentityKind;

        // Check azp (authorized party) claim — identifies the OAuth client that requested the token.
        // Service clients (machine identities) get a formatted subject and distinguished identity kind.
        string? azp = ClaimValue(GatewayAuthConventions.Claims.AuthorizedParty);
        if (azp is not null && KnownServiceClients.TryGetValue(azp, out var serviceSubject))
        {
            subject = serviceSubject;
            identityKind = GatewayAuthConventions.Audit.ServiceIdentityKind;
        }
        else
        {
            subject = ClaimValue(GatewayAuthConventions.Claims.Subject) ??
                      ClaimValue(GatewayAuthConventions.Claims.ClientId) ??
                      ClaimValue(GatewayAuthConventions.Claims.PreferredUsername) ??
                      ClaimValue(GatewayAuthConventions.Claims.Email);
        }

        return new GatewayAuditIdentity(subject, GatewayAuthConventions.Audit.OAuthAuthenticationType, identityKind);

        string? ClaimValue(string claimType)
        {
            return user.FindFirst(claimType)?.Value;
        }
    }

    private static string FormatServiceSubject(string clientId) => $"service:{clientId}";
}
