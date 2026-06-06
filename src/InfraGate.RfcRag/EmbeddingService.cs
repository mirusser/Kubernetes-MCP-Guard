using Microsoft.Extensions.AI;

namespace InfraGate.RfcRag;

/// <summary>
/// Generates vector embeddings for RFC section text using the configured embedding provider.
/// Handles batching, rate limiting, and error recovery.
/// </summary>
public sealed class EmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> generator;
    private readonly int batchSize;
    private readonly ILogger<EmbeddingService> logger;

    public EmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        int batchSize,
        ILogger<EmbeddingService> logger)
    {
        this.generator = generator;
        this.batchSize = batchSize;
        this.logger = logger;
    }

    /// <summary>
    /// Generates embeddings for a collection of text inputs, batched for efficiency.
    /// </summary>
    /// <param name="texts">Text chunks to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of embedding vectors, in the same order as the input texts.</returns>
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return [];
        }

        var results = new List<float[]>(texts.Count);

        for (int offset = 0; offset < texts.Count; offset += batchSize)
        {
            int count = Math.Min(batchSize, texts.Count - offset);
            var batch = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                batch.Add(texts[offset + i]);
            }

            logger.LogDebug(
                "Generating embeddings for batch {BatchStart}-{BatchEnd} of {TotalCount}",
                offset, offset + count, texts.Count);

            // Retry up to 3 times with exponential backoff for rate limiting
            GeneratedEmbeddings<Embedding<float>> batchResult = await RetryAsync(
                async ct => await generator.GenerateAsync(batch, null, ct).ConfigureAwait(false),
                batch.Count,
                cancellationToken).ConfigureAwait(false);

            foreach (Embedding<float> embedding in batchResult)
            {
                results.Add(embedding.Vector.ToArray());
            }
        }

        return results;
    }

    private async Task<T> RetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int batchCount,
        CancellationToken cancellationToken)
    {
        int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                int delayMs = (int)Math.Pow(2, attempt) * 1000;
                logger.LogWarning(
                    ex,
                    "Embedding request failed (attempt {Attempt}/{MaxRetries}) for batch of {BatchCount}. Retrying in {DelayMs}ms.",
                    attempt + 1, maxRetries, batchCount, delayMs);

                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        // Final attempt — let it throw if it fails
        return await operation(cancellationToken).ConfigureAwait(false);
    }
}
