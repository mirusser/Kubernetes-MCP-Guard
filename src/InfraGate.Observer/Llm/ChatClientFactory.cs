using System.ClientModel;
using System.ClientModel.Primitives;
using InfraGate.AgentLlm;
using InfraGate.Observer.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Chat;

namespace InfraGate.Observer.Llm;

internal sealed class ChatClientFactory(
    IOptions<ObserverOptions> options,
    IOptions<OpenRouterOptions> openRouterOptions,
    ILoggerFactory? loggerFactory = null) : IChatClientFactory
{
    private const string OpenRouterApiEndpoint = "https://openrouter.ai/api/v1";

    public IChatClient Create()
    {
        var provider = ParseProvider(options.Value.LlmProvider);

        if (provider == LlmProvider.Anthropic)
            throw new InvalidOperationException(
                "LlmProvider.Anthropic does not support native function calling. " +
                $"Configure {ObserverConventions.EnvironmentVariables.LlmProvider}=OpenRouter.");

        return provider switch
        {
            LlmProvider.OpenRouter => CreateOpenRouterClient(),
            _ => throw new NotSupportedException($"LLM provider '{provider}' is not yet implemented."),
        };
    }

    private IChatClient CreateOpenRouterClient()
    {
        var apiKey = openRouterOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"OpenRouter API key not configured. Set {OpenRouterOptions.ApiKeyEnvironmentVariable}.");
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
