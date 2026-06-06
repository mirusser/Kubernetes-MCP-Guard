using Microsoft.Extensions.AI;

namespace InfraGate.RfcRag.Infrastructure;

/// <summary>
/// Placeholder <see cref="IEmbeddingGenerator{TIn, TOut}"/> registered when
/// <c>InfraGate__OpenRouter__ApiKey</c> is not configured. Throws a clear error
/// only when <see cref="GenerateAsync"/> is actually called — not at DI resolve time.
/// This lets the MCP server start without an API key; only indexing fails.
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
        throw new InvalidOperationException(
            $"{RfcRagOptions.OpenRouterApiKeyEnvironmentVariable} is not configured. " +
            "Set this environment variable to enable embedding generation and RFC indexing.");
    }
}
