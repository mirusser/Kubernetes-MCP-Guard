using Microsoft.Extensions.AI;

namespace InfraGate.RfcRag.Infrastructure;

/// <summary>
/// Placeholder <see cref="IEmbeddingGenerator{TIn, TOut}"/> registered when
/// <c>InfraGate__OpenRouter__ApiKey</c> is not configured. Returns zero-vector
/// embeddings instead of throwing, so search queries fall back gracefully to
/// full-text lexical search through the hybrid RRF pipeline.
/// </summary>
internal sealed class MissingApiKeyEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public void Dispose() { }

    public object? GetService(Type? serviceType, object? key = null) => null;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        var results = new List<Embedding<float>>(list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            results.Add(new Embedding<float>(ReadOnlyMemory<float>.Empty));
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }
}
