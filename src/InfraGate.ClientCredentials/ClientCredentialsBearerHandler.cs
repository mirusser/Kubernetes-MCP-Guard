using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace InfraGate.ClientCredentials;

public sealed class ClientCredentialsBearerHandler : DelegatingHandler
{
    private readonly IClientCredentialsTokenProvider tokenProvider;
    private readonly ILogger<ClientCredentialsBearerHandler> logger;

    public ClientCredentialsBearerHandler(
        IClientCredentialsTokenProvider tokenProvider,
        ILogger<ClientCredentialsBearerHandler> logger)
    {
        this.tokenProvider = tokenProvider;
        this.logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        await AttachAuthorizationAsync(request, token, cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Got 401; refreshing client credentials token and retrying once.");
            string refreshedToken = await tokenProvider.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            var retryRequest = await CloneRequestAsync(request, cancellationToken).ConfigureAwait(false);
            await AttachAuthorizationAsync(retryRequest, refreshedToken, cancellationToken).ConfigureAwait(false);
            return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task AttachAuthorizationAsync(
        HttpRequestMessage request,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (tokenProvider is IClientCredentialsDpopProofProvider { IsDPoPEnabled: true } dpopProofProvider)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                ClientCredentialsConventions.DPoP.AuthorizationScheme,
                accessToken);
            request.Headers.Remove(ClientCredentialsConventions.DPoP.ProofHeaderName);
            string proof = await dpopProofProvider.CreateDpopProofAsync(
                accessToken,
                request,
                cancellationToken).ConfigureAwait(false);
            request.Headers.Add(ClientCredentialsConventions.DPoP.ProofHeaderName, proof);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is not null)
        {
            clone.Content = await CloneContentAsync(request.Content, cancellationToken).ConfigureAwait(false);
        }

        if (request.Headers is not null)
        {
            foreach (var (key, value) in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(key, value);
            }
        }

        foreach (var (key, value) in request.Options)
        {
            clone.Options.TryAdd(key, value);
        }

        return clone;
    }

    private static async Task<StreamContent> CloneContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        await content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        var cloned = new StreamContent(stream);
        foreach (var (key, value) in content.Headers)
        {
            cloned.Headers.TryAddWithoutValidation(key, value);
        }

        return cloned;
    }
}
