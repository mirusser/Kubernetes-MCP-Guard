using System.Diagnostics.Metrics;
using InfraGate.AgentLlm;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Llm;

internal sealed class ChatClientFactory : IChatClientFactory
{
    private readonly IOptions<ObserverOptions> options;
    private readonly Meter? meter;

    public ChatClientFactory(IOptions<ObserverOptions> options, Meter? meter = null)
    {
        this.options = options;
        this.meter = meter;
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

        const string AnthropicApiBase = "https://api.anthropic.com";
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(AnthropicApiBase),
            DefaultRequestHeaders =
            {
                { "x-api-key", apiKey },
                { "anthropic-version", "2023-06-01" },
            },
        };

        var counter = ObserverMetrics.CreateLlmTokensCounter(meter);
        return new AnthropicChatClient(httpClient, model, NullLoggerFactory.Instance, counter);
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
