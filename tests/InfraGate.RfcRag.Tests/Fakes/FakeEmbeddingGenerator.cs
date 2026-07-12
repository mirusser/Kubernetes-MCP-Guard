using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace InfraGate.RfcRag.Tests.Fakes;

internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int EmbeddingDimensions = 1536;

    // Required by the embedding generator shape even though the fake metadata is constant.
#pragma warning disable MA0041
    public EmbeddingGeneratorMetadata Metadata => new("fake", new Uri("http://localhost"));
#pragma warning restore MA0041

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        var results = new List<Embedding<float>>(list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            float[] vector = new float[EmbeddingDimensions];
            // Use SHA256 for a process-stable deterministic hash.
            // GetHashCode(StringComparison.Ordinal) is randomized per-process in .NET 7+
            // and would produce different vector orderings each test run.
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(list[i]));
            int seed = BitConverter.ToInt32(hashBytes, 0);
            for (int j = 0; j < EmbeddingDimensions; j++)
            {
                vector[j] = (float)((seed * (j + 1) * 0.001) % 1.0);
            }

            results.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type? serviceType, object? key = null) => null;

    public void Dispose()
    {
    }
}
