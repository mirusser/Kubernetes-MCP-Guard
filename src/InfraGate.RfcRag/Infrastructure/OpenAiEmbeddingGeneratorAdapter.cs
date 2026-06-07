using OpenAI;

namespace InfraGate.RfcRag.Infrastructure;

/// <summary>
/// Factory for creating an <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> backed by
/// OpenRouter (or any OpenAI-compatible endpoint). Uses the official MEAI adapter from
/// <c>Microsoft.Extensions.AI.OpenAI</c> which handles encoding format negotiation correctly.
/// </summary>
internal static class OpenAiEmbeddingGeneratorAdapter
{
    internal static IEmbeddingGenerator<string, Embedding<float>> Create(
        string apiKey, string endpoint, string model)
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), clientOptions);
        return openAiClient.GetEmbeddingClient(model).AsIEmbeddingGenerator();
    }
}
