using Microsoft.Extensions.AI;

namespace InfraGate.RfcRag.Tests.Fakes;

/// <summary>
/// Vocabulary-aware fake embedding generator.
/// Each keyword in the vocabulary activates a dedicated vector dimension with value 1.0.
/// Texts sharing keywords produce overlapping activation patterns, giving them a positive
/// cosine similarity — enabling ranking tests without a real LLM.
///
/// Design: keywords are mapped to non-overlapping dimension slots so that HTTP, TLS, URI,
/// QUIC, and normative-language domains each occupy distinct regions of the vector space.
/// </summary>
internal sealed class SemanticFakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int EmbeddingDimensions = 1536;

    private static readonly (string Keyword, int Dimension)[] Vocabulary =
    [
        // HTTP domain — dims 0–4
        ("HTTP",        0), ("REQUEST",     1), ("RESPONSE",    2), ("HEADER",     3), ("METHOD",      4),

        // TLS domain — dims 50–54
        ("TLS",        50), ("HANDSHAKE",   51), ("CERTIFICATE", 52), ("CIPHER",   53), ("RECORD",     54),

        // URI domain — dims 100–104
        ("URI",       100), ("PATH",       101), ("QUERY",      102), ("SCHEME",  103), ("AUTHORITY",  104),

        // QUIC domain — dims 150–154
        ("QUIC",      150), ("PACKET",     151), ("STREAM",     152), ("FLOW",    153), ("TRANSPORT",  154),

        // Normative-language domain — dims 200–204
        ("MUST",      200), ("SHOULD",     201), ("REQUIRE",    202), ("NORMATIVE", 203), ("RFC",      204),
    ];

#pragma warning disable MA0041
    public EmbeddingGeneratorMetadata Metadata => new("semantic-fake", new Uri("http://localhost"));
#pragma warning restore MA0041

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        var results = new List<Embedding<float>>(list.Count);

        foreach (string text in list)
        {
            float[] vector = new float[EmbeddingDimensions];
            string upper = text.ToUpperInvariant();

            foreach ((string keyword, int dim) in Vocabulary)
            {
                if (upper.Contains(keyword, StringComparison.Ordinal))
                    vector[dim] = 1.0f;
            }

            // L2-normalize so cosine similarity equals the dot product of normalized vectors.
            // Zero vectors (no matching keywords) are left as-is; pgvector treats them as
            // NaN cosine distance and ranks them last.
            float norm = MathF.Sqrt(vector.Sum(v => v * v));
            if (norm > 0f)
            {
                for (int j = 0; j < EmbeddingDimensions; j++)
                    vector[j] /= norm;
            }

            results.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type? serviceType, object? key = null) => null;

    public void Dispose() { }
}
