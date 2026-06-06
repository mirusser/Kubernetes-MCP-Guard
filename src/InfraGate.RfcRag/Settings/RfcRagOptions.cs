namespace InfraGate.RfcRag.Settings;

/// <summary>
/// Configuration options for the RFC RAG pipeline.
/// Bound from the <c>InfraGate:RfcRag</c> configuration section.
/// </summary>
public sealed class RfcRagOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "InfraGate:RfcRag";

    /// <summary>Environment variable for the OpenRouter API key (reuses existing InfraGate convention).</summary>
    public const string OpenRouterApiKeyEnvironmentVariable = "InfraGate__OpenRouter__ApiKey";

    /// <summary>Path to the local RFC mirror directory containing .txt files.</summary>
    public string RfcMirrorPath { get; set; } = string.Empty;

    /// <summary>PostgreSQL connection string for the RFC RAG database.</summary>
    public string PostgresConnectionString { get; set; } = string.Empty;

    /// <summary>OpenRouter embedding model identifier (e.g., "openai/text-embedding-3-small").</summary>
    public string EmbeddingModel { get; set; } = "openai/text-embedding-3-small";

    /// <summary>Batch size for embedding generation. Limited by OpenRouter API constraints.</summary>
    public int EmbeddingBatchSize { get; set; } = 20;

    /// <summary>Whether to run schema migrations on startup.</summary>
    public bool RunMigrationsOnStartup { get; set; } = true;

    /// <summary>OpenRouter API base URL for embedding requests.</summary>
    public string OpenRouterEmbeddingEndpoint { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// Expected vector dimension for embeddings. Must match the pgvector column dimension.
    /// Default (1536) matches text-embedding-3-small from OpenRouter/OpenAI.
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 1536;
}
