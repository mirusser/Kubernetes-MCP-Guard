using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace InfraGate.ClientCredentials;

public sealed class ClientCredentialsTokenProvider : IClientCredentialsTokenProvider
{
    private readonly ClientCredentialsTokenOptions options;
    private readonly HttpClient httpClient;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ClientCredentialsTokenProvider> logger;
    private readonly SemaphoreSlim tokenSemaphore = new(1, 1);

    private string? cachedToken;
    private DateTimeOffset tokenExpiry = DateTimeOffset.MinValue;
    private string? tokenEndpoint;

    public ClientCredentialsTokenProvider(
        ClientCredentialsTokenOptions options,
        HttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<ClientCredentialsTokenProvider> logger)
    {
        this.options = options;
        this.httpClient = httpClient;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
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

    public async Task<string> RefreshTokenAsync(CancellationToken cancellationToken)
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

    private bool IsTokenStillValid()
    {
        if (tokenExpiry == DateTimeOffset.MinValue)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var refreshThreshold = tokenExpiry - TimeSpan.FromSeconds(options.RefreshSkewSeconds);
        return now < refreshThreshold;
    }

    private async Task<string> AcquireTokenAsync(CancellationToken cancellationToken)
    {
        string endpoint = await GetTokenEndpointAsync(cancellationToken).ConfigureAwait(false);

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", options.ClientId),
            new KeyValuePair<string, string>("client_secret", options.ClientSecret ?? string.Empty),
            new KeyValuePair<string, string>("scope", options.Scope),
        ]);

        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;

        if (!root.TryGetProperty("access_token", out var accessTokenElement) ||
            string.IsNullOrWhiteSpace(accessTokenElement.GetString()))
        {
            throw new InvalidOperationException(
                "Token endpoint response did not contain a valid access_token.");
        }

        string accessToken = accessTokenElement.GetString()!;
        int expiresIn = root.TryGetProperty("expires_in", out var expiresInElement)
            ? expiresInElement.GetInt32()
            : 300;

        var now = timeProvider.GetUtcNow();
        tokenExpiry = now + TimeSpan.FromSeconds(expiresIn);
        cachedToken = accessToken;

        logger.LogInformation(
            "Client credentials token acquired. Expires at {Expiry}",
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
            .GetProperty("token_endpoint")
            .GetString()
            ?? throw new InvalidOperationException(
                "OIDC discovery document did not contain a token_endpoint.");

        return tokenEndpoint;
    }
}
