using Microsoft.Extensions.AI;
using OpenAI.Embeddings;

namespace InfraGate.RfcRag.Infrastructure;

internal sealed class OpenAiEmbeddingGeneratorAdapter : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly EmbeddingClient client;

    public OpenAiEmbeddingGeneratorAdapter(EmbeddingClient client)
    {
        this.client = client;
    }

    public static EmbeddingGeneratorMetadata Metadata => new("OpenRouter", new Uri("https://openrouter.ai"));

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        Microsoft.Extensions.AI.EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        var response = await client.GenerateEmbeddingsAsync(inputs, options: null, cancellationToken).ConfigureAwait(false);

        var results = new List<Embedding<float>>(inputs.Count);
        foreach (OpenAIEmbedding item in response.Value)
        {
            results.Add(new Embedding<float>(item.ToFloats()));
        }

        return new GeneratedEmbeddings<Embedding<float>>(results);
    }

    public object? GetService(Type? serviceType, object? key = null) => null;

    public void Dispose() { }
}
