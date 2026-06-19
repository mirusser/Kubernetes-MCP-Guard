using System.Security.Claims;
using InfraGate.McpGateway.Auth;

namespace InfraGate.McpGateway;

internal static class GatewayApprovalIdentityResolver
{
    public static GatewayApprovalIdentity? Resolve(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated is not true)
        {
            return null;
        }

        string? subject = ClaimValue(user, GatewayAuthConventions.Claims.Subject) ??
                      ClaimValue(user, GatewayAuthConventions.Claims.ClientId);
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        string displayName = ClaimValue(user, GatewayAuthConventions.Claims.PreferredUsername) ??
                          ClaimValue(user, GatewayAuthConventions.Claims.Email) ??
                          subject;

        return new GatewayApprovalIdentity(subject, displayName, user.Identity.AuthenticationType);
    }

    private static string? ClaimValue(ClaimsPrincipal user, string claimType) =>
        user.FindFirst(claimType)?.Value;
}
