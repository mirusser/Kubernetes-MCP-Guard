using InfraGate.RfcRag.Indexing;
using InfraGate.RfcRag.Parsing;
using InfraGate.RfcRag.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI;

namespace InfraGate.RfcRag.Infrastructure;

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
                dataSourceBuilder.UseVector();
                return dataSourceBuilder.Build();
            });
        }

        services.TryAddSingleton<RfcParser>();
        services.TryAddSingleton<SearchRepository>();
        services.TryAddSingleton<MetadataRepository>();
        services.TryAddSingleton<IndexingRepository>();
        services.TryAddSingleton<IIndexerService, RfcIndexer>();
        services.TryAddSingleton<ISearchService, SearchService>();

        return services.AddRfcRagEmbeddings();
    }

    public static IServiceCollection AddRfcRagEmbeddings(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var openRouterKey = Environment.GetEnvironmentVariable(
                RfcRagOptions.OpenRouterApiKeyEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(openRouterKey))
            {
                return new MissingApiKeyEmbeddingGenerator();
            }

            var options = sp.GetRequiredService<IOptions<RfcRagOptions>>().Value;
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
