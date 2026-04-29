using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace InfraGate.McpGateway;

public static class GatewayAuthentication
{
    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services, McpGatewayOptions options)
    {
        var authBuilder = services
            .AddAuthentication(authenticationOptions =>
            {
                authenticationOptions.DefaultAuthenticateScheme = McpGatewayConventions.Authentication.PolicyScheme;
                authenticationOptions.DefaultChallengeScheme = options.OAuthEnabled
                    ? McpAuthenticationDefaults.AuthenticationScheme
                    : McpGatewayConventions.Authentication.StaticBearerScheme;
            })
            .AddPolicyScheme(
                McpGatewayConventions.Authentication.PolicyScheme,
                displayName: null,
                policyOptions =>
                {
                    policyOptions.ForwardDefaultSelector = context =>
                        GatewayAuthToken.IsStaticBearerToken(context.Request.Headers.Authorization.ToString(), options)
                            ? McpGatewayConventions.Authentication.StaticBearerScheme
                            : options.OAuthEnabled
                                ? JwtBearerDefaults.AuthenticationScheme
                                : McpGatewayConventions.Authentication.StaticBearerScheme;
                })
            .AddScheme<AuthenticationSchemeOptions, StaticBearerAuthenticationHandler>(
                McpGatewayConventions.Authentication.StaticBearerScheme,
                displayName: null,
                configureOptions: null);

        if (options.OAuthEnabled)
        {
            authBuilder
                .AddJwtBearer(jwtOptions =>
                {
                    jwtOptions.Authority = options.OAuthAuthority;
                    jwtOptions.Audience = options.OAuthResource;
                    jwtOptions.MapInboundClaims = false;
                    jwtOptions.RequireHttpsMetadata = options.OAuthRequireHttpsMetadata;
                    jwtOptions.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuers = DistinctValues(options.OAuthAuthority!, TrimTrailingSlash(options.OAuthAuthority!)),
                        AudienceValidator = (audiences, _, _) => HasAudience(audiences, options.OAuthResource)
                    };
                })
                .AddMcp(mcpOptions =>
                {
                    mcpOptions.ResourceMetadataUri = new Uri(
                        McpGatewayConventions.Authentication.ProtectedResourceMetadataPath,
                        UriKind.Relative);
                    mcpOptions.ResourceMetadata = new ProtectedResourceMetadata
                    {
                        Resource = options.OAuthResource,
                        ResourceName = McpGatewayConventions.Authentication.ResourceName,
                        AuthorizationServers = { options.OAuthAuthority! },
                        ScopesSupported = { options.OAuthScope }
                    };
                });
        }

        services
            .AddAuthorizationBuilder()
            .AddPolicy(McpGatewayConventions.Authentication.PolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasRequiredScope(context.User, options.OAuthScope));
            });

        return services;
    }

    private static bool HasRequiredScope(ClaimsPrincipal user, string requiredScope)
    {
        return user.Claims
            .Where(claim => claim.Type is McpGatewayConventions.Authentication.ScopeClaim or McpGatewayConventions.Authentication.ScpClaim)
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

public sealed class StaticBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    McpGatewayOptions gatewayOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!gatewayOptions.StaticBearerEnabled)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!GatewayAuthToken.IsStaticBearerToken(authorization, gatewayOptions))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid static bearer token."));
        }

        var claims = new[]
        {
            new Claim(
                McpGatewayConventions.Authentication.SubjectClaim,
                McpGatewayConventions.GuardrailAudit.LocalBearerSubject),
            new Claim(McpGatewayConventions.Authentication.ScopeClaim, gatewayOptions.OAuthScope)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = McpGatewayConventions.AuthorizationScheme;

        return Task.CompletedTask;
    }
}

internal static class GatewayAuthToken
{
    public static bool IsStaticBearerToken(string authorization, McpGatewayOptions options)
    {
        if (!options.StaticBearerEnabled || !TryGetBearerToken(authorization, out var token))
        {
            return false;
        }

        return ConstantTimeEquals(token, options.BearerToken!);
    }

    private static bool TryGetBearerToken(string authorization, out string token)
    {
        var prefix = McpGatewayConventions.AuthorizationScheme + " ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            token = string.Empty;
            return false;
        }

        token = authorization[prefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(token);
    }

    private static bool ConstantTimeEquals(string actual, string expected)
    {
        var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);

        return actualBytes.Length == expectedBytes.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
