using Microsoft.Extensions.AI;

namespace InfraGate.Observer.Llm;

internal sealed class ChatClientFactory : IChatClientFactory
{
    private readonly IOptions<ObserverOptions> options;

    public ChatClientFactory(IOptions<ObserverOptions> options)
    {
        this.options = options;
    }

    public IChatClient Create()
    {
        var provider = ParseProvider(options.Value.LlmProvider);

        return provider switch
        {
            LlmProvider.Anthropic => CreateAnthropicClient(),
            _ => throw new NotSupportedException($"LLM provider '{provider}' is not yet implemented."),
        };
    }

    private IChatClient CreateAnthropicClient()
    {
        var apiKey = options.Value.LlmApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"LLM API key not configured. Set {ObserverConventions.EnvironmentVariables.LlmApiKey}.");
        }

        var model = string.IsNullOrWhiteSpace(options.Value.LlmModel)
            ? AnomalyObserverConventions.DefaultLlmModel
            : options.Value.LlmModel;

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.anthropic.com"),
            DefaultRequestHeaders =
            {
                { "x-api-key", apiKey },
                { "anthropic-version", "2023-06-01" },
            },
        };

        return new AnthropicChatClient(httpClient, model);
    }

    private static LlmProvider ParseProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return LlmProvider.Anthropic;
        }

        return provider.ToUpperInvariant() switch
        {
            "ANTHROPIC" => LlmProvider.Anthropic,
            "OPENAI" => LlmProvider.OpenAI,
            "GOOGLE" => LlmProvider.Google,
            "AZURE" => LlmProvider.Azure,
            "OLLAMA" => LlmProvider.Ollama,
            _ => throw new InvalidOperationException(
                $"Unknown LLM provider '{provider}'. Supported: Anthropic, OpenAI, Google, Azure, Ollama."),
        };
    }
}
