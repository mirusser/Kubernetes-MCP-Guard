namespace InfraGate.RfcRag.Indexing;

/// <summary>
/// Generates vector embeddings for RFC section text using the configured embedding provider.
/// Handles batching, concurrent dispatch, rate limiting, and error recovery.
/// </summary>
public sealed class EmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> generator;
    private readonly int batchSize;
    private readonly SemaphoreSlim throttle;
    private readonly ILogger<EmbeddingService> logger;

    public EmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        int batchSize,
        int maxConcurrency,
        ILogger<EmbeddingService> logger)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        ArgumentNullException.ThrowIfNull(logger);

        this.generator = generator;
        this.batchSize = batchSize;
        this.throttle = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        this.logger = logger;
    }

    /// <summary>
    /// Generates embeddings for a collection of text inputs.
    /// Batches are dispatched concurrently, throttled by <c>maxConcurrency</c>.
    /// </summary>
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return [];
        }

        int batchCount = (texts.Count + batchSize - 1) / batchSize;
        var batchTasks = new Task<GeneratedEmbeddings<Embedding<float>>>[batchCount];

        for (int b = 0; b < batchCount; b++)
        {
            int offset = b * batchSize;
            int count = Math.Min(batchSize, texts.Count - offset);
            string[] batch = new string[count];
            for (int i = 0; i < count; i++)
            {
                batch[i] = texts[offset + i];
            }

            batchTasks[b] = SendBatchAsync(batch, cancellationToken);
        }

        GeneratedEmbeddings<Embedding<float>>[] batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);

        float[][] results = new float[texts.Count][];
        int idx = 0;
        foreach (GeneratedEmbeddings<Embedding<float>> batchResult in batchResults)
        {
            foreach (Embedding<float> embedding in batchResult)
            {
                results[idx++] = embedding.Vector.ToArray();
            }
        }

        return results;
    }

    private async Task<GeneratedEmbeddings<Embedding<float>>> SendBatchAsync(
        string[] batch,
        CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            logger.LogDebug("Generating embeddings for batch of {BatchCount}", batch.Length);
            return await RetryAsync(
                ct => generator.GenerateAsync(batch, null, ct),
                batch.Length,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            throttle.Release();
        }
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

        return await operation(cancellationToken).ConfigureAwait(false);
    }
}
