using InfraGate.DownstreamAuth;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpServer.DownstreamAuth;

/// <summary>
/// Validates a Bearer JWT token from the downstream _meta field on MCP requests.
/// Validates: issuer (from authority), signature (from JWKS), lifetime (30s skew),
/// audience, scope claim, and azp/client_id claim (when GatewayClientId is configured).
/// </summary>
internal sealed class DownstreamTokenValidator
{
    private readonly DownstreamAuthOptions options;
    private readonly ILogger<DownstreamTokenValidator> logger;
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? configurationManager;
    private readonly SecurityKey[]? staticKeys;

    /// <summary>
    /// Production constructor — fetches JWKS from the OIDC metadata endpoint.
    /// </summary>
    internal DownstreamTokenValidator(
        DownstreamAuthOptions options,
        ILogger<DownstreamTokenValidator> logger)
    {
        this.options = options;
        this.logger = logger;

        if (options.Required)
        {
            string metadataAddress = !string.IsNullOrWhiteSpace(options.MetadataAddress)
                ? options.MetadataAddress
                : $"{options.Authority.TrimEnd('/')}/.well-known/openid-configuration";

            configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = options.RequireHttpsMetadata });
        }
    }

    /// <summary>
    /// Test constructor — accepts pre-built static signing keys (no OIDC discovery).
    /// </summary>
    internal DownstreamTokenValidator(
        DownstreamAuthOptions options,
        ILogger<DownstreamTokenValidator> logger,
        IEnumerable<SecurityKey> staticKeys)
    {
        this.options = options;
        this.logger = logger;
        this.staticKeys = [.. staticKeys];
    }

    internal async ValueTask<ValidationResult> ValidateAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (!options.Required)
        {
            return ValidationResult.Success;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return ValidationResult.Fail("Downstream auth token is missing.");
        }

        try
        {
            IEnumerable<SecurityKey> signingKeys = await ResolveSigningKeysAsync(cancellationToken)
                .ConfigureAwait(false);

            string issuer = options.Authority.TrimEnd('/');
            var validIssuers = DistinctValues(issuer, issuer + "/");

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = validIssuers,
                ValidateAudience = true,
                ValidAudience = options.Audience.TrimEnd('/'),
                ValidateLifetime = true,
                ClockSkew = DownstreamAuthConventions.Defaults.ServerClockSkew,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
            };

            var handler = new JsonWebTokenHandler();
            var result = await handler.ValidateTokenAsync(token, parameters).ConfigureAwait(false);

            if (!result.IsValid)
            {
                logger.LogWarning(
                    "Downstream token validation failed: {Reason}",
                    result.Exception?.Message ?? "unknown");

                return ValidationResult.Fail(SafeFailureReason(result));
            }

            if (!HasRequiredScope(result.ClaimsIdentity))
            {
                return ValidationResult.Fail($"Token is missing required scope '{options.Scope}'.");
            }

            if (!string.IsNullOrWhiteSpace(options.GatewayClientId) &&
                !HasExpectedClientId(result.ClaimsIdentity))
            {
                return ValidationResult.Fail(
                    $"Token azp/client_id claim does not match the expected gateway client.");
            }

            string clientId = result.ClaimsIdentity.FindFirst("azp")?.Value
                ?? result.ClaimsIdentity.FindFirst("client_id")?.Value
                ?? "(unknown)";

            // Justification: CA1873 — all log arguments are already-computed simple scalars. Negligible evaluation cost.
            logger.LogInformation(
                "Downstream auth validated, client={ClientId}",
                clientId);

            return ValidationResult.Success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Downstream token validation threw an exception");
            return ValidationResult.Fail("Downstream auth token validation failed.");
        }
    }

    private async ValueTask<IEnumerable<SecurityKey>> ResolveSigningKeysAsync(CancellationToken cancellationToken)
    {
        if (staticKeys is not null)
        {
            return staticKeys;
        }

        if (configurationManager is null)
        {
            return [];
        }

        var config = await configurationManager
            .GetConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);

        return config.SigningKeys;
    }

    private bool HasRequiredScope(System.Security.Claims.ClaimsIdentity identity)
    {
        return identity.Claims
            .Where(c => c.Type is "scope" or "scp")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(s => string.Equals(s, options.Scope, StringComparison.Ordinal));
    }

    private bool HasExpectedClientId(System.Security.Claims.ClaimsIdentity identity)
    {
        var clientId = identity.FindFirst("azp")?.Value
            ?? identity.FindFirst("client_id")?.Value;

        return string.Equals(clientId, options.GatewayClientId, StringComparison.Ordinal);
    }

    private static string SafeFailureReason(TokenValidationResult result)
    {
        // Return a safe message that does not expose token content
        return result.Exception switch
        {
            SecurityTokenExpiredException => "Downstream auth token has expired.",
            SecurityTokenInvalidAudienceException => "Downstream auth token audience is invalid.",
            SecurityTokenInvalidIssuerException => "Downstream auth token issuer is invalid.",
            SecurityTokenInvalidSignatureException => "Downstream auth token signature is invalid.",
            SecurityTokenNotYetValidException => "Downstream auth token is not yet valid.",
            _ => "Downstream auth token validation failed."
        };
    }

    private static string[] DistinctValues(params string[] values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal readonly record struct ValidationResult(bool IsValid, string? FailureReason)
    {
        internal static ValidationResult Success { get; } = new(true, null);

        internal static ValidationResult Fail(string reason) => new(false, reason);
    }
}
