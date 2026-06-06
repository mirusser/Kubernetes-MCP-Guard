using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI;

namespace InfraGate.RfcRag;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRfcRagServices(
        this IServiceCollection services,
        NpgsqlDataSource? dataSource = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (dataSource is not null)
        {
            services.TryAddSingleton(dataSource);
        }
        else
        {
            services.TryAddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
                ArgumentException.ThrowIfNullOrWhiteSpace(options.PostgresConnectionString);

                var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.PostgresConnectionString);
                return dataSourceBuilder.Build();
            });
        }

        services.TryAddSingleton<RfcParser>();
        services.TryAddSingleton<RfcRepository>();
        services.TryAddSingleton<IIndexerService, RfcIndexer>();
        services.TryAddSingleton<ISearchService, SearchService>();

        return services.AddRfcRagEmbeddings();
    }

    public static IServiceCollection AddRfcRagEmbeddings(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
            var openRouterKey = Environment.GetEnvironmentVariable(RfcRagOptions.OpenRouterApiKeyEnvironmentVariable)
                ?? throw new InvalidOperationException(
                    $"{RfcRagOptions.OpenRouterApiKeyEnvironmentVariable} environment variable is required for OpenRouter embeddings.");

            var openAiOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(options.OpenRouterEmbeddingEndpoint)
            };

            var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(openRouterKey), openAiOptions);
            var embeddingClient = client.GetEmbeddingClient(options.EmbeddingModel);
            return new OpenAiEmbeddingGeneratorAdapter(embeddingClient);
        });

        services.TryAddSingleton<EmbeddingService>(sp =>
        {
            var generator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            var options = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<EmbeddingService>>();
            return new EmbeddingService(generator, options.EmbeddingBatchSize, logger);
        });

        return services;
    }
}

internal sealed class OpenAiEmbeddingGeneratorAdapter : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly OpenAI.Embeddings.EmbeddingClient client;

    public OpenAiEmbeddingGeneratorAdapter(OpenAI.Embeddings.EmbeddingClient client)
    {
        this.client = client;
    }

    public static EmbeddingGeneratorMetadata Metadata => new("OpenRouter", new Uri("https://openrouter.ai"));

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        var response = await client.GenerateEmbeddingsAsync(inputs, options: null, cancellationToken).ConfigureAwait(false);

        var results = new List<Embedding<float>>(inputs.Count);
        foreach (OpenAI.Embeddings.OpenAIEmbedding item in response.Value)
        {
            results.Add(new Embedding<float>(item.ToFloats()));
        }

        return new GeneratedEmbeddings<Embedding<float>>(results);
    }

    public object? GetService(Type? serviceType, object? key = null) => null;

    public void Dispose() { }
}
