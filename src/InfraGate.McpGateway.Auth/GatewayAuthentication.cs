using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

        var authBuilder = AddGatewayAuthenticationSchemes(services, options);
        AddOAuthAuthentication(authBuilder, options);
        AddGatewayAuthorization(services, options);

        return services;
    }

    private static AuthenticationBuilder AddGatewayAuthenticationSchemes(
        IServiceCollection services,
        GatewayAuthOptions options)
    {
        return services
            .AddAuthentication(authenticationOptions =>
            {
                authenticationOptions.DefaultAuthenticateScheme = GatewayAuthConventions.Schemes.PolicyScheme;
                authenticationOptions.DefaultChallengeScheme = DefaultChallengeScheme(options);
                authenticationOptions.DefaultForbidScheme = GatewayAuthConventions.Schemes.PolicyScheme;
            })
            .AddPolicyScheme(
                GatewayAuthConventions.Schemes.PolicyScheme,
                displayName: null,
                policyOptions =>
                {
                    policyOptions.ForwardDefaultSelector = context => ForwardedAuthenticationScheme(context, options);
                })
            .AddScheme<AuthenticationSchemeOptions, StaticBearerAuthenticationHandler>(
                GatewayAuthConventions.Schemes.StaticBearer,
                displayName: null,
                configureOptions: null);
    }

    private static void AddOAuthAuthentication(AuthenticationBuilder authBuilder, GatewayAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OAuthAuthority))
        {
            return;
        }

        var oauthAuthority = options.OAuthAuthority;
        authBuilder
            .AddJwtBearer(jwtOptions => ConfigureJwtBearerOptions(jwtOptions, options, oauthAuthority))
            .AddMcp(mcpOptions => ConfigureMcpOptions(mcpOptions, options, oauthAuthority));
    }

    private static void AddGatewayAuthorization(IServiceCollection services, GatewayAuthOptions options)
    {
        services
            .AddAuthorizationBuilder()
            .AddPolicy(GatewayAuthConventions.Schemes.PolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasRequiredScope(context.User, options.OAuthScope));
            });
    }

    private static string DefaultChallengeScheme(GatewayAuthOptions options) =>
        options.OAuthEnabled
            ? McpAuthenticationDefaults.AuthenticationScheme
            : GatewayAuthConventions.Schemes.StaticBearer;

    private static string ForwardedAuthenticationScheme(HttpContext context, GatewayAuthOptions options)
    {
        if (GatewayAuthToken.IsStaticBearerToken(context.Request.Headers.Authorization.ToString(), options))
        {
            return GatewayAuthConventions.Schemes.StaticBearer;
        }

        return options.OAuthEnabled
            ? JwtBearerDefaults.AuthenticationScheme
            : GatewayAuthConventions.Schemes.StaticBearer;
    }

    private static void ConfigureJwtBearerOptions(
        JwtBearerOptions jwtOptions,
        GatewayAuthOptions options,
        string oauthAuthority)
    {
        jwtOptions.Authority = oauthAuthority;
        if (!string.IsNullOrWhiteSpace(options.OAuthMetadataAddress))
        {
            jwtOptions.MetadataAddress = options.OAuthMetadataAddress;
        }

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
        jwtOptions.Events = CreateJwtBearerEvents(options);
    }

    private static JwtBearerEvents CreateJwtBearerEvents(GatewayAuthOptions options) => new()
    {
        OnForbidden = context =>
        {
            var resourceMetadata = ResourceMetadataUrl(context.Request);
            context.Response.Headers.WWWAuthenticate =
                BuildInsufficientScopeChallenge(options.OAuthScope, resourceMetadata);

            return Task.CompletedTask;
        }
    };

    private static void ConfigureMcpOptions(
        McpAuthenticationOptions mcpOptions,
        GatewayAuthOptions options,
        string oauthAuthority)
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
    }

    private static string ResourceMetadataUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{GatewayAuthConventions.Metadata.ProtectedResourcePath}";

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

    private static string BuildInsufficientScopeChallenge(string scope, string resourceMetadata)
    {
        return $"{GatewayAuthConventions.AuthorizationScheme} " +
               $"{GatewayAuthConventions.ChallengeParameters.Error}=\"{GatewayAuthConventions.OAuthErrors.InsufficientScope}\", " +
               $"{GatewayAuthConventions.ChallengeParameters.Scope}=\"{scope}\", " +
               $"{GatewayAuthConventions.ChallengeParameters.ResourceMetadata}=\"{resourceMetadata}\"";
    }

    private static string[] DistinctValues(params string[] values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
