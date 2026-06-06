using System.Security.Claims;
using InfraGate.McpGateway.Auth.Dpop;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace InfraGate.McpGateway.Auth;

public static class GatewayAuthentication
{
    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services, GatewayAuthOptions options)
    {
        services.AddSingleton(options);

        if (options.RequireDPoP)
        {
            services.AddSingleton<IDpopProofReplayStore, InMemoryDpopProofReplayStore>();
            services.AddSingleton<IDpopProofValidator, DpopProofValidator>();
        }

        var authBuilder = AddGatewayAuthenticationSchemes(services);
        AddJwtBearerAuthentication(authBuilder, options);
        AddApprovalBrowserAuthentication(authBuilder, options);
        AddGatewayAuthorization(services, options);

        return services;
    }

    private static AuthenticationBuilder AddGatewayAuthenticationSchemes(IServiceCollection services)
    {
        return services
            .AddAuthentication(authenticationOptions =>
            {
                authenticationOptions.DefaultAuthenticateScheme = GatewayAuthConventions.Schemes.PolicyScheme;
                authenticationOptions.DefaultChallengeScheme = DefaultChallengeScheme();
                authenticationOptions.DefaultForbidScheme = GatewayAuthConventions.Schemes.PolicyScheme;
            })
            .AddPolicyScheme(
                GatewayAuthConventions.Schemes.PolicyScheme,
                displayName: null,
                policyOptions =>
                {
                    policyOptions.ForwardDefaultSelector = _ => ForwardedAuthenticationScheme();
                });
    }

    private static void AddJwtBearerAuthentication(AuthenticationBuilder authBuilder, GatewayAuthOptions options)
    {
        var oauthAuthority = options.OAuthAuthority;
        authBuilder
            .AddJwtBearer(jwtOptions => ConfigureJwtBearerOptions(jwtOptions, options, oauthAuthority))
            .AddMcp(mcpOptions => ConfigureMcpOptions(mcpOptions, options, oauthAuthority));
    }

    private static void AddApprovalBrowserAuthentication(AuthenticationBuilder authBuilder, GatewayAuthOptions options)
    {
        authBuilder
            .AddCookie(
                GatewayAuthConventions.Schemes.ApprovalCookie,
                cookieOptions =>
                {
                    cookieOptions.LoginPath = GatewayAuthConventions.Approvals.LoginPath;
                    cookieOptions.Cookie.Name = GatewayAuthConventions.Approvals.CookieName;
                    cookieOptions.Cookie.HttpOnly = true;
                    cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
                })
            .AddOAuth(
                GatewayAuthConventions.Schemes.ApprovalOAuth,
                oauthOptions => ConfigureApprovalOAuthOptions(oauthOptions, options));
    }

    internal static readonly string[] AcceptedScopes =
    [
        GatewayAuthConventions.DefaultOAuthScope,
        GatewayAuthConventions.DefaultReadOnlyOAuthScope,
        GatewayAuthConventions.DefaultProposeOAuthScope,
        GatewayAuthConventions.DefaultExecuteOAuthScope,
        GatewayAuthConventions.DefaultReadToolsOAuthScope,
        GatewayAuthConventions.DefaultWriteToolsOAuthScope
    ];

    private static void AddGatewayAuthorization(IServiceCollection services, GatewayAuthOptions options)
    {
        services
            .AddAuthorizationBuilder()
            .AddPolicy(GatewayAuthConventions.Schemes.PolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasAnyAcceptedScope(context.User, options.OAuthScope));
            })
            .AddPolicy(GatewayAuthConventions.Schemes.ApprovalPolicyName, policy =>
            {
                policy.AuthenticationSchemes.Add(GatewayAuthConventions.Schemes.ApprovalCookie);
                policy.RequireAuthenticatedUser();
            });
    }

    private static string DefaultChallengeScheme() =>
        McpAuthenticationDefaults.AuthenticationScheme;

    private static string ForwardedAuthenticationScheme()
        => JwtBearerDefaults.AuthenticationScheme;

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
        OnMessageReceived = context =>
        {
            // Support Authorization: DPoP <token> alongside the default Authorization: Bearer <token>
            var authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith(
                    $"{GatewayAuthConventions.DPoP.Scheme} ",
                    StringComparison.OrdinalIgnoreCase))
            {
                context.Token = authorization[$"{GatewayAuthConventions.DPoP.Scheme} ".Length..].Trim();
            }
            return Task.CompletedTask;
        },

        OnTokenValidated = async context =>
        {
            if (!options.RequireDPoP)
                return;

            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith(
                    $"{GatewayAuthConventions.DPoP.Scheme} ",
                    StringComparison.OrdinalIgnoreCase))
            {
                context.Fail("DPoP is required: use 'Authorization: DPoP <token>'.");
                return;
            }

            var dpopProof = context.Request.Headers[GatewayAuthConventions.DPoP.ProofHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(dpopProof))
            {
                context.Fail("DPoP proof header is missing.");
                return;
            }

            if (context.SecurityToken is not JsonWebToken accessToken)
            {
                context.Fail("Unexpected security token type.");
                return;
            }

            var request = context.Request;
            var uri = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}";
            var validator = context.HttpContext.RequestServices
                .GetRequiredService<IDpopProofValidator>();

            var result = await validator.ValidateAsync(
                new DpopProofValidationContext(
                    DpopProofJwt: dpopProof,
                    AccessToken: accessToken.EncodedToken,
                    HttpMethod: request.Method,
                    HttpUri: uri),
                context.HttpContext.RequestAborted).ConfigureAwait(false);

            if (!result.IsValid)
                context.Fail($"DPoP validation failed: {result.FailureReason}");
        },

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
        var metadata = new ProtectedResourceMetadata
        {
            Resource = options.OAuthResource,
            ResourceName = GatewayAuthConventions.Metadata.ResourceName,
            AuthorizationServers = { oauthAuthority }
        };
        foreach (string scope in DistinctValues([options.OAuthScope, .. AcceptedScopes]))
        {
            metadata.ScopesSupported.Add(scope);
        }

        mcpOptions.ResourceMetadata = metadata;
    }

    private static void ConfigureApprovalOAuthOptions(OAuthOptions oauthOptions, GatewayAuthOptions options)
    {
        oauthOptions.SignInScheme = GatewayAuthConventions.Schemes.ApprovalCookie;
        oauthOptions.ClientId = options.ApprovalOAuthClientId;
        oauthOptions.ClientSecret = GatewayAuthConventions.Approvals.PublicClientSecretPlaceholder;
        oauthOptions.CallbackPath = options.ApprovalOAuthCallbackPath;
        oauthOptions.AuthorizationEndpoint = options.ApprovalAuthorizationEndpoint;
        oauthOptions.TokenEndpoint = options.ApprovalTokenEndpoint;
        oauthOptions.UsePkce = true;
        oauthOptions.SaveTokens = false;
        oauthOptions.Scope.Clear();
        oauthOptions.Scope.Add(options.OAuthScope);
        oauthOptions.Events = CreateApprovalOAuthEvents(options);
    }

    private static OAuthEvents CreateApprovalOAuthEvents(GatewayAuthOptions options) => new()
    {
        OnRedirectToAuthorizationEndpoint = context =>
        {
            var redirectUri = QueryHelpers.AddQueryString(
                context.RedirectUri,
                GatewayAuthConventions.Parameters.Resource,
                options.OAuthResource);
            context.Response.Redirect(redirectUri);

            return Task.CompletedTask;
        },
        OnCreatingTicket = context =>
        {
            if (string.IsNullOrWhiteSpace(context.AccessToken))
            {
                context.Fail("OAuth token response did not contain an access token.");
                return Task.CompletedTask;
            }

            var token = new JsonWebTokenHandler().ReadJsonWebToken(context.AccessToken);
            foreach (var claim in token.Claims)
            {
                context.Identity?.AddClaim(claim);
            }

            return Task.CompletedTask;
        }
    };

    private static string ResourceMetadataUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{GatewayAuthConventions.Metadata.ProtectedResourcePath}";

    internal static bool HasAnyAcceptedScope(ClaimsPrincipal user, string primaryScope)
    {
        var userScopes = user.Claims
            .Where(claim => claim.Type is GatewayAuthConventions.Claims.Scope or GatewayAuthConventions.Claims.Scp)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);

        return userScopes.Contains(primaryScope) ||
               AcceptedScopes.Any(userScopes.Contains);
    }

    public static bool HasRequiredScope(ClaimsPrincipal user, string requiredScope)
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
