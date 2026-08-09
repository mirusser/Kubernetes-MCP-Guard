using Microsoft.IdentityModel.Protocols;
using System.Threading.Channels;

namespace InfraGate.McpGateway.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IDocumentRetriever"/> that counts fetches, can fail the next
/// request, and returns a sequence of JWKS documents after the discovery document.
/// </summary>
internal sealed class CountingDocumentRetriever : IDocumentRetriever
{
    private readonly string discoveryDocument;
    private readonly string[] jwksDocuments;
    private readonly Channel<int> fetchCounts = Channel.CreateUnbounded<int>();
    private int fetchCount;
    private int jwksIndex;
    private int failNext;

    public CountingDocumentRetriever(string discoveryDocument, params string[] jwksDocuments)
    {
        this.discoveryDocument = discoveryDocument;
        this.jwksDocuments = jwksDocuments;
    }

    public int FetchCount => Volatile.Read(ref fetchCount);

    public void FailNext() => Interlocked.Exchange(ref failNext, 1);

    public async Task WaitForFetchCountAsync(int expectedCount, CancellationToken cancellationToken)
    {
        while (FetchCount < expectedCount)
        {
            await fetchCounts.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        int currentFetchCount = Interlocked.Increment(ref fetchCount);
        fetchCounts.Writer.TryWrite(currentFetchCount);
        if (Interlocked.Exchange(ref failNext, 0) != 0)
        {
            return Task.FromException<string>(new InvalidOperationException("JWKS fetch failed"));
        }

        if (address.EndsWith("openid-configuration", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(discoveryDocument);
        }

        int currentJwksIndex = Interlocked.Increment(ref jwksIndex) - 1;
        string document = jwksDocuments[Math.Min(currentJwksIndex, jwksDocuments.Length - 1)];

        return Task.FromResult(document);
    }
}
