using InfraGate.RfcRag.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.RfcRag.Tests.UnitTests;

public sealed class EmbeddingServiceTests
{
    [Fact]
    public async Task GenerateEmbeddingsAsync_MultipleTexts_ReturnsCorrectCount()
    {
        var generator = new FakeEmbeddingGenerator();
        var service = new EmbeddingService(generator, 2, NullLogger<EmbeddingService>.Instance);

        var texts = new[] { "text1", "text2", "text3", "text4", "text5" };
        var embeddings = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        Assert.Equal(5, embeddings.Count);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_EmptyList_ReturnsEmpty()
    {
        var generator = new FakeEmbeddingGenerator();
        var service = new EmbeddingService(generator, 2, NullLogger<EmbeddingService>.Instance);

        var texts = Array.Empty<string>();
        var embeddings = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        Assert.Empty(embeddings);
    }

    [Fact]
    public void Constructor_BatchSizeZero_ThrowsArgumentOutOfRangeException()
    {
        var generator = new FakeEmbeddingGenerator();
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmbeddingService(generator, 0, NullLogger<EmbeddingService>.Instance));
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_PredictableValues_MapsCorrectly()
    {
        var generator = new FakeEmbeddingGenerator();
        var service = new EmbeddingService(generator, 2, NullLogger<EmbeddingService>.Instance);

        var texts = new[] { "test" };
        var embeddings = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        Assert.Single(embeddings);
        Assert.Equal(1536, embeddings[0].Length);
    }
}
