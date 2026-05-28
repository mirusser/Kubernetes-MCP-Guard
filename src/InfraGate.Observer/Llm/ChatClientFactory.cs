using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.Metrics;
using InfraGate.AgentLlm;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Chat;

namespace InfraGate.Observer.Llm;

internal sealed class ChatClientFactory(IOptions<ObserverOptions> options, Meter? meter = null, ILoggerFactory? loggerFactory = null) : IChatClientFactory
{
    private const string OpenRouterApiEndpoint = "https://openrouter.ai/api/v1";

    public IChatClient Create()
    {
        var provider = ParseProvider(options.Value.LlmProvider);

        return provider switch
        {
            LlmProvider.Anthropic => CreateAnthropicClient(),
            LlmProvider.OpenRouter => CreateOpenRouterClient(),
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

        ObserverLogEvents.LogLlmProviderConfigured(
            loggerFactory?.CreateLogger(nameof(ChatClientFactory)) ?? NullLogger.Instance,
            "Anthropic",
            model);

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

    private IChatClient CreateOpenRouterClient()
    {
        var apiKey = options.Value.LlmApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"LLM API key not configured. Set {ObserverConventions.EnvironmentVariables.LlmApiKey}.");
        }

        var model = string.IsNullOrWhiteSpace(options.Value.LlmModel)
            ? AnomalyObserverConventions.DefaultOpenRouterLlmModel
            : options.Value.LlmModel;

        ObserverLogEvents.LogLlmProviderConfigured(
            loggerFactory?.CreateLogger(nameof(ChatClientFactory)) ?? NullLogger.Instance,
            "OpenRouter",
            model);

        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(OpenRouterApiEndpoint) };
        clientOptions.AddPolicy(OpenRouterPipelinePolicy.Default, PipelinePosition.PerCall);
        var chatClient = new ChatClient(model, new ApiKeyCredential(apiKey), clientOptions);

        return new RateLimitRetryingChatClient(chatClient.AsIChatClient(), loggerFactory?.CreateLogger<RateLimitRetryingChatClient>());
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
            ObserverConventions.LlmProviders.OpenRouter => LlmProvider.OpenRouter,
            _ => throw new InvalidOperationException(
                $"Unknown LLM provider '{provider}'. Supported: Anthropic, OpenAI, Google, Azure, Ollama, OpenRouter."),
        };
    }
}
