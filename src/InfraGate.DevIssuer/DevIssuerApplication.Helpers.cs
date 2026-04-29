using System.Text.Json.Serialization;

namespace InfraGate.DevIssuer;

internal static partial class DevIssuerApplication
{
    private static IResult OAuthError(string error, string description)
    {
        return Results.Json(
            new OAuthErrorResponse(error, description),
            statusCode: StatusCodes.Status400BadRequest);
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

    private sealed record OAuthErrorResponse(
        [property: JsonPropertyName(DevIssuerConventions.Json.Error)] string Error,
        [property: JsonPropertyName(DevIssuerConventions.Json.ErrorDescription)] string ErrorDescription);
}
