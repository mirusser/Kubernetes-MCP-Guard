using InfraGate.RfcRag.Indexing;
using InfraGate.RfcRag.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.RfcRag.Tests.UnitTests;

public sealed class EmbeddingServiceTests
{
    [Fact]
    public async Task GenerateEmbeddingsAsync_MultipleTexts_ReturnsCorrectCount()
    {
        var generator = new FakeEmbeddingGenerator();
        var service = new EmbeddingService(generator, 2, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);

        var texts = new[] { "text1", "text2", "text3", "text4", "text5" };
        var embeddings = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        Assert.Equal(5, embeddings.Count);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_EmptyList_ReturnsEmpty()
    {
        var generator = new FakeEmbeddingGenerator();
        var service = new EmbeddingService(generator, 2, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);

        var texts = Array.Empty<string>();
        var embeddings = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        Assert.Empty(embeddings);
    }

    [Fact]
    public void Constructor_BatchSizeZero_ThrowsArgumentOutOfRangeException()
    {
        var generator = new FakeEmbeddingGenerator();
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmbeddingService(generator, 0, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance));
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_PredictableValues_MapsCorrectly()
    {
        var generator = new FakeEmbeddingGenerator();
        var service = new EmbeddingService(generator, 2, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);

        var texts = new[] { "test" };
        var embeddings = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        Assert.Single(embeddings);
        Assert.Equal(1536, embeddings[0].Length);
    }

    // --- Batching tests ---

    [Fact]
    public async Task Batching_11Texts_BatchSize5_Makes3GeneratorCalls()
    {
        var generator = new TrackingEmbeddingGenerator();
        var service = new EmbeddingService(generator, batchSize: 5, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);

        var texts = Enumerable.Range(0, 11).Select(i => $"text{i}").ToArray();
        await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        // ceil(11 / 5) = 3
        Assert.Equal(3, generator.CallCount);
    }

    [Fact]
    public async Task Batching_11Texts_BatchSize5_LastBatchHasRemainder()
    {
        var generator = new TrackingEmbeddingGenerator();
        var service = new EmbeddingService(generator, batchSize: 5, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);

        var texts = Enumerable.Range(0, 11).Select(i => $"text{i}").ToArray();
        await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        // Batches: [0..4]=5, [5..9]=5, [10]=1
        Assert.Equal(new[] { 5, 5, 1 }, generator.BatchSizes);
    }

    [Fact]
    public async Task ConcurrentBatches_PreserveInputOrder()
    {
        // 12 texts, batch=3, maxConcurrency=4 — four batches can run in parallel.
        // Task.WhenAll preserves task-index order in its result array, so the stitching
        // in EmbeddingService must also iterate in task order (not completion order).
        var service = new EmbeddingService(
            new FakeEmbeddingGenerator(), batchSize: 3, maxConcurrency: 4,
            NullLogger<EmbeddingService>.Instance);
        var refService = new EmbeddingService(
            new FakeEmbeddingGenerator(), batchSize: 1, maxConcurrency: 1,
            NullLogger<EmbeddingService>.Instance);

        var texts = Enumerable.Range(0, 12).Select(i => $"ordering-sentinel-{i:00}").ToArray();
        IReadOnlyList<float[]> batched = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        for (int i = 0; i < texts.Length; i++)
        {
            IReadOnlyList<float[]> single = await refService.GenerateEmbeddingsAsync([texts[i]], CancellationToken.None);
            Assert.Equal(single[0], batched[i]);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_Deterministic_SameInputProducesSameOutput()
    {
        var service = new EmbeddingService(
            new FakeEmbeddingGenerator(), batchSize: 5, maxConcurrency: 1,
            NullLogger<EmbeddingService>.Instance);
        string[] texts = ["the quick brown fox", "TLS certificate handshake", "HTTP semantics"];

        IReadOnlyList<float[]> first = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);
        IReadOnlyList<float[]> second = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        for (int i = 0; i < texts.Length; i++)
            Assert.Equal(first[i], second[i]);
    }
}
