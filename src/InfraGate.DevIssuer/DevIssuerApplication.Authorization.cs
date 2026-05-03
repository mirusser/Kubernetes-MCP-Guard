using Microsoft.AspNetCore.WebUtilities;

namespace InfraGate.DevIssuer;

internal static partial class DevIssuerApplication
{
    private static IResult Authorize(HttpRequest request, DevIssuerOptions options, DevIssuerStore store)
    {
        var authorizationRequest = AuthorizationRequest.From(request.Query);
        var validation = ValidateAuthorizationRequest(authorizationRequest, options, store);
        if (validation is not null)
        {
            return validation;
        }

        var authorizationCode = store.CreateAuthorizationCode(
            authorizationRequest.ClientId!,
            authorizationRequest.RedirectUri!,
            authorizationRequest.CodeChallenge!,
            options.Resource,
            options.Scope,
            DateTimeOffset.UtcNow.Add(authorizationCodeLifetime));

        return RedirectWithAuthorizationCode(authorizationRequest, authorizationCode.Code);
    }

    private static IResult? ValidateAuthorizationRequest(
        AuthorizationRequest request,
        DevIssuerOptions options,
        DevIssuerStore store) =>
        ValidateResponseType(request) ??
        ValidateRequiredAuthorizationParameters(request) ??
        ValidateCodeChallengeMethod(request) ??
        ValidateRegisteredClient(request, store) ??
        ValidateAuthorizationResource(request, options) ??
        ValidateAuthorizationScope(request, options);

    private static IResult? ValidateResponseType(AuthorizationRequest request)
    {
        return string.Equals(request.ResponseType, DevIssuerConventions.OAuth.CodeResponseType, StringComparison.Ordinal)
            ? null
            : OAuthError(DevIssuerConventions.Errors.UnsupportedResponseType, "response_type must be code.");
    }

    private static IResult? ValidateRequiredAuthorizationParameters(AuthorizationRequest request)
    {
        return HasRequiredAuthorizationParameters(request)
            ? null
            : OAuthError(DevIssuerConventions.Errors.InvalidRequest, "client_id, redirect_uri, code_challenge, resource, and scope are required.");
    }

    private static bool HasRequiredAuthorizationParameters(AuthorizationRequest request) =>
        !string.IsNullOrWhiteSpace(request.ClientId) &&
        !string.IsNullOrWhiteSpace(request.RedirectUri) &&
        !string.IsNullOrWhiteSpace(request.CodeChallenge) &&
        !string.IsNullOrWhiteSpace(request.Resource) &&
        !string.IsNullOrWhiteSpace(request.Scope);

    private static IResult? ValidateCodeChallengeMethod(AuthorizationRequest request)
    {
        return string.Equals(request.CodeChallengeMethod, DevIssuerConventions.OAuth.S256CodeChallengeMethod, StringComparison.Ordinal)
            ? null
            : OAuthError(DevIssuerConventions.Errors.InvalidRequest, "Only S256 PKCE is supported.");
    }

    private static IResult? ValidateRegisteredClient(AuthorizationRequest request, DevIssuerStore store)
    {
        return store.ClientAllowsRedirectUri(request.ClientId!, request.RedirectUri!)
            ? null
            : OAuthError(DevIssuerConventions.Errors.UnauthorizedClient, "Unknown client_id or redirect_uri.");
    }

    private static IResult? ValidateAuthorizationResource(AuthorizationRequest request, DevIssuerOptions options)
    {
        return ResourceMatches(request.Resource!, options.Resource)
            ? null
            : OAuthError(DevIssuerConventions.Errors.InvalidRequest, "resource does not match this dev issuer.");
    }

    private static IResult? ValidateAuthorizationScope(AuthorizationRequest request, DevIssuerOptions options)
    {
        return ContainsScope(request.Scope!, options.Scope)
            ? null
            : OAuthError(DevIssuerConventions.Errors.InvalidScope, "Required scope is missing.");
    }

    private static IResult RedirectWithAuthorizationCode(AuthorizationRequest request, string code)
    {
        var redirectParameters = new Dictionary<string, string?>
        {
            [DevIssuerConventions.Parameters.Code] = code
        };
        if (!string.IsNullOrWhiteSpace(request.State))
        {
            redirectParameters[DevIssuerConventions.Parameters.State] = request.State;
        }

        return Results.Redirect(QueryHelpers.AddQueryString(request.RedirectUri!, redirectParameters));
    }

    private sealed record AuthorizationRequest(
        string? ResponseType,
        string? ClientId,
        string? RedirectUri,
        string? CodeChallenge,
        string? CodeChallengeMethod,
        string? Resource,
        string? Scope,
        string? State)
    {
        public static AuthorizationRequest From(IQueryCollection query) => new(
            QueryValue(query, DevIssuerConventions.Parameters.ResponseType),
            QueryValue(query, DevIssuerConventions.Parameters.ClientId),
            QueryValue(query, DevIssuerConventions.Parameters.RedirectUri),
            QueryValue(query, DevIssuerConventions.Parameters.CodeChallenge),
            QueryValue(query, DevIssuerConventions.Parameters.CodeChallengeMethod),
            QueryValue(query, DevIssuerConventions.Parameters.Resource),
            QueryValue(query, DevIssuerConventions.Parameters.Scope),
            QueryValue(query, DevIssuerConventions.Parameters.State));
    }
}
