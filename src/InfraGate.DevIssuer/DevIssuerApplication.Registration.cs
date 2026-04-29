using System.Text.Json.Serialization;

namespace InfraGate.DevIssuer;

internal static partial class DevIssuerApplication
{
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
}
