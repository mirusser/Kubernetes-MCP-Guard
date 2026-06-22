using Microsoft.IdentityModel.Protocols;

namespace InfraGate.McpGateway.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IDocumentRetriever"/> that counts fetches, can fail the next
/// request, and returns a sequence of JWKS documents after the discovery document.
/// </summary>
internal sealed class CountingDocumentRetriever : IDocumentRetriever
{
    private readonly string discoveryDocument;
    private readonly string[] jwksDocuments;
    private int jwksIndex;
    private bool failNext;

    public CountingDocumentRetriever(string discoveryDocument, params string[] jwksDocuments)
    {
        this.discoveryDocument = discoveryDocument;
        this.jwksDocuments = jwksDocuments;
    }

    public int FetchCount { get; private set; }

    public void FailNext() => failNext = true;

    public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        FetchCount++;
        if (failNext)
        {
            failNext = false;
            return Task.FromException<string>(new InvalidOperationException("JWKS fetch failed"));
        }

        if (address.EndsWith("openid-configuration", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(discoveryDocument);
        }

        string document = jwksDocuments[Math.Min(jwksIndex, jwksDocuments.Length - 1)];
        if (jwksIndex < jwksDocuments.Length - 1)
        {
            jwksIndex++;
        }

        return Task.FromResult(document);
    }
}
