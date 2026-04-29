using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InfraGate.DevIssuer;

internal static class DevIssuerApplication
{
    private static readonly TimeSpan AuthorizationCodeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(30);

    public static IServiceCollection AddDevIssuer(this IServiceCollection services, DevIssuerOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<DevIssuerStore>();
        services.AddSingleton<DevIssuerSigningKey>();

        return services;
    }

    public static IEndpointRouteBuilder MapDevIssuer(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            DevIssuerConventions.Endpoints.AuthorizationServerMetadata,
            (DevIssuerOptions options) => Results.Json(CreateMetadata(options)));
        endpoints.MapGet(
            DevIssuerConventions.Endpoints.OpenIdConfiguration,
            (DevIssuerOptions options) => Results.Json(CreateMetadata(options)));
        endpoints.MapGet(
            DevIssuerConventions.Endpoints.Jwks,
            (DevIssuerSigningKey signingKey) => Results.Json(signingKey.CreateJwks()));
        endpoints.MapPost(DevIssuerConventions.Endpoints.Register, RegisterClientAsync);
        endpoints.MapGet(DevIssuerConventions.Endpoints.Authorize, Authorize);
        endpoints.MapPost(DevIssuerConventions.Endpoints.Token, TokenAsync);

        return endpoints;
    }

    private static IDictionary<string, object?> CreateMetadata(DevIssuerOptions options)
    {
        return new Dictionary<string, object?>
        {
            [DevIssuerConventions.Json.Issuer] = TrimTrailingSlash(options.Issuer),
            [DevIssuerConventions.Json.AuthorizationEndpoint] = Endpoint(options, DevIssuerConventions.Endpoints.Authorize),
            [DevIssuerConventions.Json.TokenEndpoint] = Endpoint(options, DevIssuerConventions.Endpoints.Token),
            [DevIssuerConventions.Json.JwksUri] = Endpoint(options, DevIssuerConventions.Endpoints.Jwks),
            [DevIssuerConventions.Json.RegistrationEndpoint] = Endpoint(options, DevIssuerConventions.Endpoints.Register),
            [DevIssuerConventions.Json.ResponseTypesSupported] = new[] { DevIssuerConventions.OAuth.CodeResponseType },
            [DevIssuerConventions.Json.GrantTypesSupported] = new[] { DevIssuerConventions.OAuth.AuthorizationCodeGrantType },
            [DevIssuerConventions.Json.CodeChallengeMethodsSupported] = new[] { DevIssuerConventions.OAuth.S256CodeChallengeMethod },
            [DevIssuerConventions.Json.TokenEndpointAuthMethodsSupported] = new[] { DevIssuerConventions.OAuth.NoneAuthenticationMethod },
            [DevIssuerConventions.Json.ScopesSupported] = new[] { options.Scope },
            [DevIssuerConventions.Json.SubjectTypesSupported] = new[] { DevIssuerConventions.OAuth.PublicSubjectType },
            [DevIssuerConventions.Json.IdTokenSigningAlgValuesSupported] = new[] { DevIssuerConventions.OAuth.RsaSha256Algorithm }
        };
    }

    private static async Task<IResult> RegisterClientAsync(HttpRequest request, DevIssuerStore store, CancellationToken cancellationToken)
    {
        var registration = await request.ReadFromJsonAsync<RegistrationRequest>(cancellationToken);
        if (registration?.RedirectUris is not { Length: > 0 } redirectUris ||
            redirectUris.Any(redirectUri => !IsLoopbackHttpUri(redirectUri)))
        {
            return OAuthError(
                DevIssuerConventions.Errors.InvalidRequest,
                "redirect_uris must contain at least one loopback http URI.");
        }

        var client = store.RegisterClient(redirectUris, registration.ClientName);
        var response = new RegistrationResponse(
            client.ClientId,
            client.ClientName,
            client.RedirectUris,
            [DevIssuerConventions.OAuth.AuthorizationCodeGrantType],
            [DevIssuerConventions.OAuth.CodeResponseType],
            DevIssuerConventions.OAuth.NoneAuthenticationMethod);

        return Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    private static IResult Authorize(HttpRequest request, DevIssuerOptions options, DevIssuerStore store)
    {
        var query = request.Query;
        var responseType = QueryValue(query, DevIssuerConventions.Parameters.ResponseType);
        var clientId = QueryValue(query, DevIssuerConventions.Parameters.ClientId);
        var redirectUri = QueryValue(query, DevIssuerConventions.Parameters.RedirectUri);
        var codeChallenge = QueryValue(query, DevIssuerConventions.Parameters.CodeChallenge);
        var codeChallengeMethod = QueryValue(query, DevIssuerConventions.Parameters.CodeChallengeMethod);
        var resource = QueryValue(query, DevIssuerConventions.Parameters.Resource);
        var scope = QueryValue(query, DevIssuerConventions.Parameters.Scope);
        var state = QueryValue(query, DevIssuerConventions.Parameters.State);

        if (!string.Equals(responseType, DevIssuerConventions.OAuth.CodeResponseType, StringComparison.Ordinal))
        {
            return OAuthError(DevIssuerConventions.Errors.UnsupportedResponseType, "response_type must be code.");
        }

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(redirectUri) ||
            string.IsNullOrWhiteSpace(codeChallenge) ||
            string.IsNullOrWhiteSpace(resource) ||
            string.IsNullOrWhiteSpace(scope))
        {
            return OAuthError(DevIssuerConventions.Errors.InvalidRequest, "client_id, redirect_uri, code_challenge, resource, and scope are required.");
        }

        if (!string.Equals(codeChallengeMethod, DevIssuerConventions.OAuth.S256CodeChallengeMethod, StringComparison.Ordinal))
        {
            return OAuthError(DevIssuerConventions.Errors.InvalidRequest, "Only S256 PKCE is supported.");
        }

        if (!store.ClientAllowsRedirectUri(clientId, redirectUri))
        {
            return OAuthError(DevIssuerConventions.Errors.UnauthorizedClient, "Unknown client_id or redirect_uri.");
        }

        if (!ResourceMatches(resource, options.Resource))
        {
            return OAuthError(DevIssuerConventions.Errors.InvalidRequest, "resource does not match this dev issuer.");
        }

        if (!ContainsScope(scope, options.Scope))
        {
            return OAuthError(DevIssuerConventions.Errors.InvalidScope, "Required scope is missing.");
        }

        var authorizationCode = store.CreateAuthorizationCode(
            clientId,
            redirectUri,
            codeChallenge,
            options.Resource,
            options.Scope,
            DateTimeOffset.UtcNow.Add(AuthorizationCodeLifetime));
        var redirectParameters = new Dictionary<string, string?>
        {
            [DevIssuerConventions.Parameters.Code] = authorizationCode.Code
        };
        if (!string.IsNullOrWhiteSpace(state))
        {
            redirectParameters[DevIssuerConventions.Parameters.State] = state;
        }

        return Results.Redirect(QueryHelpers.AddQueryString(redirectUri, redirectParameters));
    }

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
            string.IsNullOrWhiteSpace(codeVerifier) ||
            string.IsNullOrWhiteSpace(resource))
        {
            return OAuthError(DevIssuerConventions.Errors.InvalidRequest, "code, redirect_uri, client_id, code_verifier, and resource are required.");
        }

        if (!store.TryConsumeAuthorizationCode(code, IsValidAuthorizationCode, out var authorizationCode))
        {
            return OAuthError(DevIssuerConventions.Errors.InvalidGrant, "Authorization code is invalid, expired, reused, or failed PKCE validation.");
        }

        var now = DateTime.UtcNow;
        var expires = now.Add(AccessTokenLifetime);
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
            (int)AccessTokenLifetime.TotalSeconds,
            authorizationCode.Scope));

        bool IsValidAuthorizationCode(AuthorizationCode authorizationCode)
        {
            return authorizationCode.ExpiresAt > DateTimeOffset.UtcNow &&
                   string.Equals(authorizationCode.ClientId, clientId, StringComparison.Ordinal) &&
                   string.Equals(authorizationCode.RedirectUri, redirectUri, StringComparison.Ordinal) &&
                   ResourceMatches(resource, authorizationCode.Resource) &&
                   PkceMatches(codeVerifier, authorizationCode.CodeChallenge);
        }
    }

    private static IResult OAuthError(string error, string description)
    {
        return Results.Json(
            new OAuthErrorResponse(error, description),
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static string Endpoint(DevIssuerOptions options, string path)
    {
        return TrimTrailingSlash(options.Issuer) + path;
    }

    private static string? QueryValue(IQueryCollection query, string name)
    {
        return query.TryGetValue(name, out var value) ? value.ToString() : null;
    }

    private static string? FormValue(IFormCollection form, string name)
    {
        return form.TryGetValue(name, out var value) ? value.ToString() : null;
    }

    private static bool ContainsScope(string scopeValue, string requiredScope)
    {
        return scopeValue
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(scope => string.Equals(scope, requiredScope, StringComparison.Ordinal));
    }

    private static bool ResourceMatches(string actual, string expected)
    {
        return string.Equals(TrimTrailingSlash(actual), TrimTrailingSlash(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSlash(string value)
    {
        return value.TrimEnd('/');
    }

    private static bool IsLoopbackHttpUri(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttp &&
               uri.IsLoopback;
    }

    private static bool PkceMatches(string codeVerifier, string codeChallenge)
    {
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var computedChallenge = Base64UrlEncoder.Encode(challengeBytes);

        return string.Equals(computedChallenge, codeChallenge, StringComparison.Ordinal);
    }

    private sealed record RegistrationRequest(
        [property: JsonPropertyName(DevIssuerConventions.Json.RedirectUris)] string[]? RedirectUris,
        [property: JsonPropertyName(DevIssuerConventions.Json.ClientName)] string? ClientName);

    private sealed record RegistrationResponse(
        [property: JsonPropertyName(DevIssuerConventions.Json.ClientId)] string ClientId,
        [property: JsonPropertyName(DevIssuerConventions.Json.ClientName)] string? ClientName,
        [property: JsonPropertyName(DevIssuerConventions.Json.RedirectUris)] IReadOnlyCollection<string> RedirectUris,
        [property: JsonPropertyName(DevIssuerConventions.Json.GrantTypes)] IReadOnlyCollection<string> GrantTypes,
        [property: JsonPropertyName(DevIssuerConventions.Json.ResponseTypes)] IReadOnlyCollection<string> ResponseTypes,
        [property: JsonPropertyName(DevIssuerConventions.Json.TokenEndpointAuthMethod)] string TokenEndpointAuthMethod);

    private sealed record TokenResponse(
        [property: JsonPropertyName(DevIssuerConventions.Json.AccessToken)] string AccessToken,
        [property: JsonPropertyName(DevIssuerConventions.Json.TokenType)] string TokenType,
        [property: JsonPropertyName(DevIssuerConventions.Json.ExpiresIn)] int ExpiresIn,
        [property: JsonPropertyName(DevIssuerConventions.Json.Scope)] string Scope);

    private sealed record OAuthErrorResponse(
        [property: JsonPropertyName(DevIssuerConventions.Json.Error)] string Error,
        [property: JsonPropertyName(DevIssuerConventions.Json.ErrorDescription)] string ErrorDescription);
}
