using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;

namespace InfraGate.McpGateway.Auth;

internal sealed class HttpTokenIntrospectionClient(
    HttpClient httpClient,
    GatewayAuthOptions options) : ITokenIntrospectionClient
{
    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        AllowTrailingCommas = true
    };

    private readonly SemaphoreSlim discoveryLock = new(1, 1);
    private string? discoveredEndpoint;

    public async Task<TokenIntrospectionResult> IntrospectAsync(
        JsonWebToken accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);

        var endpoint = await ResolveEndpointAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(options.TokenIntrospectionClientId) ||
            string.IsNullOrWhiteSpace(options.TokenIntrospectionClientSecret))
        {
            return TokenIntrospectionResult.Inactive;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = CreateBasicAuthenticationHeader(
            options.TokenIntrospectionClientId,
            options.TokenIntrospectionClientSecret);
        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>(
                GatewayAuthConventions.TokenIntrospection.TokenFormFieldName,
                accessToken.EncodedToken)
        ]);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return TokenIntrospectionResult.Inactive;
        }
        catch (InvalidOperationException)
        {
            return TokenIntrospectionResult.Inactive;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TokenIntrospectionResult.Inactive;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return TokenIntrospectionResult.Inactive;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    JsonDocumentOptions,
                    cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                if (!root.TryGetProperty(
                        GatewayAuthConventions.TokenIntrospection.ActiveResponsePropertyName,
                        out var activeProperty) ||
                    activeProperty.ValueKind is not JsonValueKind.True)
                {
                    return TokenIntrospectionResult.Inactive;
                }

                DateTimeOffset? expiresAt = null;
                if (root.TryGetProperty(GatewayAuthConventions.Claims.Expiration, out var expirationProperty) &&
                    expirationProperty.TryGetInt64(out long expirationSeconds))
                {
                    try
                    {
                        expiresAt = DateTimeOffset.FromUnixTimeSeconds(expirationSeconds);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return TokenIntrospectionResult.Inactive;
                    }
                }

                return TokenIntrospectionResult.Active(expiresAt);
            }
            catch (JsonException)
            {
                return TokenIntrospectionResult.Inactive;
            }
        }
    }

    private async Task<string?> ResolveEndpointAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.TokenIntrospectionEndpoint))
        {
            return options.TokenIntrospectionEndpoint;
        }

        if (!string.IsNullOrWhiteSpace(discoveredEndpoint))
        {
            return discoveredEndpoint;
        }

        await discoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(discoveredEndpoint))
            {
                return discoveredEndpoint;
            }

            discoveredEndpoint = await DiscoverEndpointAsync(cancellationToken).ConfigureAwait(false);
            return discoveredEndpoint;
        }
        finally
        {
            discoveryLock.Release();
        }
    }

    private async Task<string?> DiscoverEndpointAsync(CancellationToken cancellationToken)
    {
        var metadataAddress = !string.IsNullOrWhiteSpace(options.OAuthMetadataAddress)
            ? options.OAuthMetadataAddress
            : TrimTrailingSlash(options.OAuthAuthority) + GatewayAuthConventions.TokenIntrospection.OpenIdConnectDiscoveryPath;

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(metadataAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    JsonDocumentOptions,
                    cancellationToken).ConfigureAwait(false);
                return document.RootElement.TryGetProperty(
                           GatewayAuthConventions.TokenIntrospection.IntrospectionEndpointMetadataName,
                           out var endpointProperty) &&
                       endpointProperty.ValueKind is JsonValueKind.String
                    ? endpointProperty.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static AuthenticationHeaderValue CreateBasicAuthenticationHeader(string clientId, string clientSecret)
    {
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        return new AuthenticationHeaderValue(
            GatewayAuthConventions.TokenIntrospection.BasicAuthenticationScheme,
            credentials);
    }

    private static string TrimTrailingSlash(string value) => value.TrimEnd('/');
}
