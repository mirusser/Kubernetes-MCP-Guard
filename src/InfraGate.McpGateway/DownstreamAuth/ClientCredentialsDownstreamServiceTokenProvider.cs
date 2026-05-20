using System.Text.Json;
using InfraGate.DownstreamAuth;
using Microsoft.Extensions.Logging;

namespace InfraGate.McpGateway.DownstreamAuth;

internal sealed class ClientCredentialsDownstreamServiceTokenProvider : IDownstreamServiceTokenProvider
{
    private readonly DownstreamAuthOptions options;
    private readonly HttpClient httpClient;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ClientCredentialsDownstreamServiceTokenProvider> logger;
    private readonly SemaphoreSlim tokenSemaphore = new(1, 1);

    private string? cachedToken;
    private DateTimeOffset tokenExpiry = DateTimeOffset.MinValue;
    private string? tokenEndpoint;

    public ClientCredentialsDownstreamServiceTokenProvider(
        DownstreamAuthOptions options,
        HttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<ClientCredentialsDownstreamServiceTokenProvider> logger)
    {
        this.options = options;
        this.httpClient = httpClient;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async Task<string> GetServiceTokenAsync(CancellationToken cancellationToken)
    {
        if (cachedToken is not null && IsTokenStillValid())
        {
            return cachedToken;
        }

        await tokenSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cachedToken is not null && IsTokenStillValid())
            {
                return cachedToken;
            }

            return await AcquireTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            tokenSemaphore.Release();
        }
    }

    private bool IsTokenStillValid()
    {
        if (tokenExpiry == DateTimeOffset.MinValue)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var refreshThreshold = tokenExpiry - DownstreamAuthConventions.Defaults.GatewayRefreshSkew;
        return now < refreshThreshold;
    }

    public async Task<string> RefreshServiceTokenAsync(CancellationToken cancellationToken)
    {
        await tokenSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await AcquireTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            tokenSemaphore.Release();
        }
    }

    private async Task<string> AcquireTokenAsync(CancellationToken cancellationToken)
    {
        string endpoint = await GetTokenEndpointAsync(cancellationToken).ConfigureAwait(false);

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>(TokenRequestParameters.GrantType, TokenRequestParameters.ClientCredentials),
            new KeyValuePair<string, string>(TokenRequestParameters.ClientId, options.GatewayClientId),
            new KeyValuePair<string, string>(TokenRequestParameters.ClientSecret, options.GatewayClientSecret ?? string.Empty),
            new KeyValuePair<string, string>(TokenRequestParameters.Scope, options.Scope),
        ]);

        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;

        if (!root.TryGetProperty(TokenResponseFields.AccessToken, out var accessTokenElement) ||
            string.IsNullOrWhiteSpace(accessTokenElement.GetString()))
        {
            throw new InvalidOperationException(
                "Downstream token endpoint response did not contain a valid access_token.");
        }

        string accessToken = accessTokenElement.GetString()!;
        int expiresIn = root.TryGetProperty(TokenResponseFields.ExpiresIn, out var expiresInElement)
            ? expiresInElement.GetInt32()
            : 300;

        var now = timeProvider.GetUtcNow();
        tokenExpiry = now + TimeSpan.FromSeconds(expiresIn);
        cachedToken = DownstreamAuthConventions.BearerPrefix + accessToken;

        logger.LogInformation(
            "Downstream service token acquired. Expires at {Expiry}",
            tokenExpiry);

        return cachedToken;
    }

    private async Task<string> GetTokenEndpointAsync(CancellationToken cancellationToken)
    {
        if (tokenEndpoint is not null)
        {
            return tokenEndpoint;
        }

        string metadataAddress = !string.IsNullOrWhiteSpace(options.MetadataAddress)
            ? options.MetadataAddress
            : $"{options.Authority.TrimEnd('/')}/.well-known/openid-configuration";

        using var response = await httpClient.GetAsync(metadataAddress, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

        tokenEndpoint = document.RootElement
            .GetProperty(OidcDiscoveryFields.TokenEndpoint)
            .GetString()
            ?? throw new InvalidOperationException(
                "OIDC discovery document did not contain a token_endpoint.");

        return tokenEndpoint;
    }

    private static class TokenRequestParameters
    {
        public const string GrantType = "grant_type";
        public const string ClientCredentials = "client_credentials";
        public const string ClientId = "client_id";
        public const string ClientSecret = "client_secret";
        public const string Scope = "scope";
    }

    private static class TokenResponseFields
    {
        public const string AccessToken = "access_token";
        public const string ExpiresIn = "expires_in";
    }

    private static class OidcDiscoveryFields
    {
        public const string TokenEndpoint = "token_endpoint";
    }
}
