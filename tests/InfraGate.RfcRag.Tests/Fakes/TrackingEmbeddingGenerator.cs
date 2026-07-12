using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace InfraGate.RfcRag.Tests.Fakes;

/// <summary>
/// Records per-call batch sizes so tests can assert that EmbeddingService
/// correctly splits inputs into batches of the configured size.
/// Produces the same deterministic vectors as FakeEmbeddingGenerator so
/// ordering tests can compare batched vs. single-item results.
/// </summary>
internal sealed class TrackingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int EmbeddingDimensions = 1536;
    private readonly List<int> batchSizes = [];

    public IReadOnlyList<int> BatchSizes => batchSizes;
    public int CallCount => batchSizes.Count;

#pragma warning disable MA0041
    public EmbeddingGeneratorMetadata Metadata => new("tracking", new Uri("http://localhost"));
#pragma warning restore MA0041

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        lock (batchSizes)
        {
            batchSizes.Add(list.Count);
        }

        var results = new List<Embedding<float>>(list.Count);
        foreach (string text in list)
        {
            float[] vector = new float[EmbeddingDimensions];
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            int seed = BitConverter.ToInt32(hash, 0);
            for (int j = 0; j < EmbeddingDimensions; j++)
                vector[j] = (float)((seed * (j + 1) * 0.001) % 1.0);
            results.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type? serviceType, object? key = null) => null;

    public void Dispose() { }
}
