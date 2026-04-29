using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.DevIssuer;

internal static partial class DevIssuerApplication
{
    private static async Task<IResult> TokenAsync(
        HttpRequest request,
        DevIssuerOptions options,
        DevIssuerStore store,
        DevIssuerSigningKey signingKey,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return OAuthError(DevIssuerConventions.Errors.InvalidRequest, "Token requests must use form encoding.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var grantType = FormValue(form, DevIssuerConventions.Parameters.GrantType);
        var code = FormValue(form, DevIssuerConventions.Parameters.Code);
        var redirectUri = FormValue(form, DevIssuerConventions.Parameters.RedirectUri);
        var clientId = FormValue(form, DevIssuerConventions.Parameters.ClientId);
        var codeVerifier = FormValue(form, DevIssuerConventions.Parameters.CodeVerifier);
        var resource = FormValue(form, DevIssuerConventions.Parameters.Resource);

        if (!string.Equals(grantType, DevIssuerConventions.OAuth.AuthorizationCodeGrantType, StringComparison.Ordinal))
        {
            return OAuthError(DevIssuerConventions.Errors.UnsupportedGrantType, "grant_type must be authorization_code.");
        }

        if (string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(redirectUri) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(codeVerifier))
        {
            return OAuthError(DevIssuerConventions.Errors.InvalidRequest, "code, redirect_uri, client_id, and code_verifier are required.");
        }

        if (!store.TryConsumeAuthorizationCode(code, IsValidAuthorizationCode, out var authorizationCode))
        {
            return OAuthError(DevIssuerConventions.Errors.InvalidGrant, "Authorization code is invalid, expired, reused, or failed PKCE validation.");
        }

        var now = DateTime.UtcNow;
        var expires = now.Add(accessTokenLifetime);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TrimTrailingSlash(options.Issuer),
            Audience = options.Resource,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(DevIssuerConventions.Claims.Subject, options.Subject)
            ]),
            Claims = new Dictionary<string, object>
            {
                [DevIssuerConventions.Claims.Scope] = authorizationCode.Scope,
                [DevIssuerConventions.Claims.ClientId] = clientId,
                [DevIssuerConventions.Claims.PreferredUsername] = options.Subject,
                [DevIssuerConventions.Claims.JwtId] = Guid.NewGuid().ToString("N")
            },
            SigningCredentials = signingKey.SigningCredentials
        };
        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);

        return Results.Json(new TokenResponse(
            accessToken,
            DevIssuerConventions.OAuth.BearerTokenType,
            (int)accessTokenLifetime.TotalSeconds,
            authorizationCode.Scope));

        bool IsValidAuthorizationCode(AuthorizationCode authorizationCode)
        {
            return authorizationCode.ExpiresAt > DateTimeOffset.UtcNow &&
                   string.Equals(authorizationCode.ClientId, clientId, StringComparison.Ordinal) &&
                   string.Equals(authorizationCode.RedirectUri, redirectUri, StringComparison.Ordinal) &&
                   TokenResourceMatches(resource, authorizationCode.Resource) &&
                   PkceMatches(codeVerifier, authorizationCode.CodeChallenge);
        }
    }

    private static bool TokenResourceMatches(string? actual, string expected)
    {
        return string.IsNullOrWhiteSpace(actual) || ResourceMatches(actual, expected);
    }

    private static bool PkceMatches(string codeVerifier, string codeChallenge)
    {
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var computedChallenge = Base64UrlEncoder.Encode(challengeBytes);

        return string.Equals(computedChallenge, codeChallenge, StringComparison.Ordinal);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName(DevIssuerConventions.Json.AccessToken)] string AccessToken,
        [property: JsonPropertyName(DevIssuerConventions.Json.TokenType)] string TokenType,
        [property: JsonPropertyName(DevIssuerConventions.Json.ExpiresIn)] int ExpiresIn,
        [property: JsonPropertyName(DevIssuerConventions.Json.Scope)] string Scope);
}
