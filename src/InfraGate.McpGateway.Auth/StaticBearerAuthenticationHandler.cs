using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraGate.McpGateway.Auth;

public sealed class StaticBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    GatewayAuthOptions gatewayOptions)
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
                GatewayAuthConventions.Claims.Subject,
                GatewayAuthConventions.Audit.LocalBearerSubject),
            new Claim(GatewayAuthConventions.Claims.Scope, gatewayOptions.OAuthScope)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = GatewayAuthConventions.AuthorizationScheme;

        return Task.CompletedTask;
    }
}
