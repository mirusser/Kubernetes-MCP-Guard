using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.McpGateway.Auth.Dpop;

internal sealed record class DpopProofValidationContext(
    string DpopProofJwt,
    string AccessToken,
    string HttpMethod,
    string HttpUri);

internal sealed record class DpopProofValidationResult
{
    public bool IsValid { get; init; }
    public string? FailureReason { get; init; }

    internal static DpopProofValidationResult Success() => new() { IsValid = true };
    internal static DpopProofValidationResult Failure(string reason) => new() { IsValid = false, FailureReason = reason };
}

internal interface IDpopProofValidator
{
    Task<DpopProofValidationResult> ValidateAsync(
        DpopProofValidationContext context,
        CancellationToken cancellationToken = default);
}

internal sealed class DpopProofValidator : IDpopProofValidator
{
    private static readonly TimeSpan MaxProofAge = TimeSpan.FromSeconds(300);

    private static readonly HashSet<string> AsymmetricAlgorithms = new(StringComparer.Ordinal)
    {
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512
    };

    private readonly IDpopProofReplayStore replayStore;

    public DpopProofValidator(IDpopProofReplayStore replayStore)
    {
        ArgumentNullException.ThrowIfNull(replayStore);
        this.replayStore = replayStore;
    }

    public async Task<DpopProofValidationResult> ValidateAsync(
        DpopProofValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var parts = context.DpopProofJwt.Split('.');
        if (parts.Length != 3)
            return DpopProofValidationResult.Failure("DPoP proof is not a valid JWT.");

        JsonElement header;
        JsonElement payload;
        try
        {
            header = ParseBase64UrlJson(parts[0]);
            payload = ParseBase64UrlJson(parts[1]);
        }
        catch
        {
            return DpopProofValidationResult.Failure("DPoP proof contains a malformed header or payload.");
        }

        // typ must be "dpop+jwt"
        if (!header.TryGetProperty("typ", out var typEl) ||
            !string.Equals(typEl.GetString(), GatewayAuthConventions.DPoP.ProofTyp, StringComparison.Ordinal))
            return DpopProofValidationResult.Failure(
                $"DPoP proof typ must be '{GatewayAuthConventions.DPoP.ProofTyp}'.");

        // alg must be asymmetric
        if (!header.TryGetProperty("alg", out var algEl) || !IsAsymmetric(algEl.GetString()))
            return DpopProofValidationResult.Failure(
                "DPoP proof must use an asymmetric signing algorithm.");

        // jwk must be present and must not include private key material
        if (!header.TryGetProperty("jwk", out var jwkEl))
            return DpopProofValidationResult.Failure("DPoP proof header is missing the jwk member.");

        if (jwkEl.TryGetProperty("d", out _))
            return DpopProofValidationResult.Failure(
                "DPoP proof header jwk must not contain private key material.");

        JsonWebKey proofJwk;
        try
        {
            proofJwk = new JsonWebKey(jwkEl.GetRawText());
        }
        catch
        {
            return DpopProofValidationResult.Failure("DPoP proof header contains an invalid jwk.");
        }

        // Verify signature using the embedded public key. DPoP proofs are self-signed: no issuer and no audience.
        // Expiration is optional for DPoP proofs, but exp/nbf must be honored when present.
        var handler = new JsonWebTokenHandler();
#pragma warning disable CA5404 // Issuer and audience validation are intentionally disabled for self-signed DPoP proofs.
        var signatureResult = await handler.ValidateTokenAsync(
            context.DpopProofJwt,
            new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                RequireExpirationTime = false,
                RequireSignedTokens = true,
                IssuerSigningKey = proofJwk
            }).ConfigureAwait(false);
#pragma warning restore CA5404

        if (!signatureResult.IsValid)
            return DpopProofValidationResult.Failure("DPoP proof signature is invalid.");

        // Required payload claims
        if (!TryGetString(payload, "jti", out var jti) || string.IsNullOrWhiteSpace(jti))
            return DpopProofValidationResult.Failure("DPoP proof is missing the jti claim.");

        if (!TryGetString(payload, "htm", out var htm))
            return DpopProofValidationResult.Failure("DPoP proof is missing the htm claim.");

        if (!TryGetString(payload, "htu", out var htu))
            return DpopProofValidationResult.Failure("DPoP proof is missing the htu claim.");

        if (!TryGetLong(payload, "iat", out var iat))
            return DpopProofValidationResult.Failure("DPoP proof is missing the iat claim.");

        if (!TryGetString(payload, "ath", out var ath))
            return DpopProofValidationResult.Failure("DPoP proof is missing the ath claim.");

        // htm must match request method
        if (!string.Equals(htm, context.HttpMethod, StringComparison.OrdinalIgnoreCase))
            return DpopProofValidationResult.Failure(
                "DPoP proof htm does not match the request method.");

        // htu must match request URI (without query or fragment)
        if (!HtuMatches(htu, context.HttpUri))
            return DpopProofValidationResult.Failure(
                "DPoP proof htu does not match the request URI.");

        // iat must be within the acceptable window
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - iat) > (long)MaxProofAge.TotalSeconds)
            return DpopProofValidationResult.Failure(
                "DPoP proof iat is outside the acceptable time window.");

        // ath must equal base64url(SHA256(access_token))
        var expectedAth = ComputeAth(context.AccessToken);
        if (!string.Equals(ath, expectedAth, StringComparison.Ordinal))
            return DpopProofValidationResult.Failure(
                "DPoP proof ath does not match the access token hash.");

        // cnf.jkt in the access token must match the proof JWK thumbprint
        var cnfJkt = ExtractCnfJkt(context.AccessToken);
        if (cnfJkt is null)
            return DpopProofValidationResult.Failure(
                "Access token is missing the cnf.jkt claim.");

        var proofThumbprint = ComputeJwkThumbprint(proofJwk);
        if (!string.Equals(cnfJkt, proofThumbprint, StringComparison.Ordinal))
            return DpopProofValidationResult.Failure(
                "DPoP proof key thumbprint does not match the access token cnf.jkt.");

        var replayKey = ExtractReplayKey(context.AccessToken);
        if (replayKey is null)
            return DpopProofValidationResult.Failure(
                "Access token is missing issuer or presenter claim.");

        // Check replay store last — only record the jti when all other checks pass
        if (!await replayStore.TryAddAsync(
                replayKey.Value.Issuer,
                replayKey.Value.Presenter,
                jti!,
                MaxProofAge,
                cancellationToken).ConfigureAwait(false))
            return DpopProofValidationResult.Failure("DPoP proof jti has already been used.");

        return DpopProofValidationResult.Success();
    }

    private static JsonElement ParseBase64UrlJson(string base64Url)
    {
        var bytes = Base64UrlEncoder.DecodeBytes(base64Url);
        return JsonDocument.Parse(bytes).RootElement;
    }

    private static bool IsAsymmetric(string? alg) =>
        alg is not null && AsymmetricAlgorithms.Contains(alg);

    private static bool TryGetString(JsonElement element, string name, [NotNullWhen(true)] out string? value)
    {
        if (element.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString();
            return value is not null;
        }
        value = null;
        return false;
    }

    private static bool TryGetLong(JsonElement element, string name, out long value)
    {
        if (element.TryGetProperty(name, out var el) && el.TryGetInt64(out value))
            return true;
        value = 0;
        return false;
    }

    private static bool HtuMatches(string proofHtu, string requestUri)
    {
        if (!Uri.TryCreate(proofHtu, UriKind.Absolute, out var proofUri) ||
            !Uri.TryCreate(requestUri, UriKind.Absolute, out var reqUri))
            return false;

        // Compare scheme + host + path only; strip query and fragment
        return string.Equals(proofUri.GetLeftPart(UriPartial.Path),
            reqUri.GetLeftPart(UriPartial.Path),
            StringComparison.OrdinalIgnoreCase);
    }

    internal static string ComputeAth(string accessToken)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        return Base64UrlEncoder.Encode(bytes);
    }

    internal static string ComputeJwkThumbprint(JsonWebKey jwk)
    {
        // RFC 7638: required members only, alphabetical order, no whitespace
        var canonical = jwk.Kty switch
        {
            "EC" => $"{{\"crv\":\"{jwk.Crv}\",\"kty\":\"EC\",\"x\":\"{jwk.X}\",\"y\":\"{jwk.Y}\"}}",
            "RSA" => $"{{\"e\":\"{jwk.E}\",\"kty\":\"RSA\",\"n\":\"{jwk.N}\"}}",
            _ => throw new NotSupportedException($"Unsupported JWK key type for thumbprint: {jwk.Kty}")
        };
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64UrlEncoder.Encode(bytes);
    }

    private static string? ExtractCnfJkt(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
            return null;
        try
        {
            var payload = ParseBase64UrlJson(parts[1]);
            if (!payload.TryGetProperty(GatewayAuthConventions.DPoP.CnfClaim, out var cnf))
                return null;
            if (!cnf.TryGetProperty(GatewayAuthConventions.DPoP.JktClaim, out var jkt))
                return null;
            return jkt.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static DpopReplayKey? ExtractReplayKey(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
            return null;

        try
        {
            var payload = ParseBase64UrlJson(parts[1]);
            if (!TryGetString(payload, "iss", out var issuer) || string.IsNullOrWhiteSpace(issuer))
                return null;

            if (!TryGetString(payload, GatewayAuthConventions.Claims.AuthorizedParty, out var presenter) &&
                !TryGetString(payload, GatewayAuthConventions.Claims.ClientId, out presenter) &&
                !TryGetString(payload, GatewayAuthConventions.Claims.Subject, out presenter))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(presenter)
                ? null
                : new DpopReplayKey(issuer, presenter);
        }
        catch
        {
            return null;
        }
    }

    private readonly record struct DpopReplayKey(string Issuer, string Presenter);
}
