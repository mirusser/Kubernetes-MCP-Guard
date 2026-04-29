using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace InfraGate.McpGateway.Auth;

public static class GatewayAuthentication
{
    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services, GatewayAuthOptions options)
    {
        services.AddSingleton(options);

        var authBuilder = services
            .AddAuthentication(authenticationOptions =>
            {
                authenticationOptions.DefaultAuthenticateScheme = GatewayAuthConventions.Schemes.PolicyScheme;
                authenticationOptions.DefaultChallengeScheme = options.OAuthEnabled
                    ? McpAuthenticationDefaults.AuthenticationScheme
                    : GatewayAuthConventions.Schemes.StaticBearer;
            })
            .AddPolicyScheme(
                GatewayAuthConventions.Schemes.PolicyScheme,
                displayName: null,
                policyOptions =>
                {
                    policyOptions.ForwardDefaultSelector = context =>
                        GatewayAuthToken.IsStaticBearerToken(context.Request.Headers.Authorization.ToString(), options)
                            ? GatewayAuthConventions.Schemes.StaticBearer
                            : options.OAuthEnabled
                                ? JwtBearerDefaults.AuthenticationScheme
                                : GatewayAuthConventions.Schemes.StaticBearer;
                })
            .AddScheme<AuthenticationSchemeOptions, StaticBearerAuthenticationHandler>(
                GatewayAuthConventions.Schemes.StaticBearer,
                displayName: null,
                configureOptions: null);

        if (!string.IsNullOrWhiteSpace(options.OAuthAuthority))
        {
            var oauthAuthority = options.OAuthAuthority;
            authBuilder
                .AddJwtBearer(jwtOptions =>
                {
                    jwtOptions.Authority = oauthAuthority;
                    jwtOptions.Audience = options.OAuthResource;
                    jwtOptions.MapInboundClaims = false;
                    jwtOptions.RequireHttpsMetadata = options.OAuthRequireHttpsMetadata;
                    jwtOptions.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuers = DistinctValues(oauthAuthority, TrimTrailingSlash(oauthAuthority)),
                        AudienceValidator = (audiences, _, _) => HasAudience(audiences, options.OAuthResource)
                    };
                })
                .AddMcp(mcpOptions =>
                {
                    mcpOptions.ResourceMetadataUri = new Uri(
                        GatewayAuthConventions.Metadata.ProtectedResourcePath,
                        UriKind.Relative);
                    mcpOptions.ResourceMetadata = new ProtectedResourceMetadata
                    {
                        Resource = options.OAuthResource,
                        ResourceName = GatewayAuthConventions.Metadata.ResourceName,
                        AuthorizationServers = { oauthAuthority },
                        ScopesSupported = { options.OAuthScope }
                    };
                });
        }

        services
            .AddAuthorizationBuilder()
            .AddPolicy(GatewayAuthConventions.Schemes.PolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasRequiredScope(context.User, options.OAuthScope));
            });

        return services;
    }

    private static bool HasRequiredScope(ClaimsPrincipal user, string requiredScope)
    {
        return user.Claims
            .Where(claim => claim.Type is GatewayAuthConventions.Claims.Scope or GatewayAuthConventions.Claims.Scp)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(scope => string.Equals(scope, requiredScope, StringComparison.Ordinal));
    }

    private static bool HasAudience(IEnumerable<string>? audiences, string expectedAudience)
    {
        if (audiences is null)
        {
            return false;
        }

        var normalizedExpected = TrimTrailingSlash(expectedAudience);

        return audiences.Any(audience =>
            string.Equals(TrimTrailingSlash(audience), normalizedExpected, StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimTrailingSlash(string value) => value.TrimEnd('/');

    private static string[] DistinctValues(params string[] values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
