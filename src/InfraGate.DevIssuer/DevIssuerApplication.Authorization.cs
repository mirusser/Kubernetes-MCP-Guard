using Microsoft.AspNetCore.WebUtilities;

namespace InfraGate.DevIssuer;

internal static partial class DevIssuerApplication
{
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
            DateTimeOffset.UtcNow.Add(authorizationCodeLifetime));
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
}
