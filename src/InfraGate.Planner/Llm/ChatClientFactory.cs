using System.ClientModel;
using System.ClientModel.Primitives;
using InfraGate.AgentLlm;
using InfraGate.Planner.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Chat;

namespace InfraGate.Planner.Llm;

internal sealed class ChatClientFactory(IOptions<PlannerOptions> options, ILoggerFactory? loggerFactory = null) : IChatClientFactory
{
    private const string OpenRouterApiEndpoint = "https://openrouter.ai/api/v1";

    public IChatClient Create()
    {
        var provider = ParseProvider(options.Value.LlmProvider);

        if (provider == LlmProvider.Anthropic)
            throw new InvalidOperationException(
                "LlmProvider.Anthropic does not support native function calling. " +
                $"Configure {PlannerConventions.EnvironmentVariables.LlmProvider}=OpenRouter.");

        return provider switch
        {
            LlmProvider.OpenRouter => CreateOpenRouterClient(),
#pragma warning disable MA0025
            _ => throw new NotImplementedException($"LLM provider '{provider}' is not yet implemented."),
#pragma warning restore MA0025
        };
    }

    private IChatClient CreateAnthropicClient()
    {
        var apiKey = options.Value.LlmApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"LLM API key not configured. Set {PlannerConventions.EnvironmentVariables.LlmApiKey}.");
        }

        var model = string.IsNullOrWhiteSpace(options.Value.LlmModel)
            ? PlannerConventions.DefaultLlmModel
            : options.Value.LlmModel;

        PlannerLogEvents.LogLlmProviderConfigured(
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

        return new AnthropicChatClient(httpClient, model, NullLoggerFactory.Instance);
    }

    private IChatClient CreateOpenRouterClient()
    {
        var apiKey = options.Value.LlmApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"LLM API key not configured. Set {PlannerConventions.EnvironmentVariables.LlmApiKey}.");
        }

        var model = string.IsNullOrWhiteSpace(options.Value.LlmModel)
            ? PlannerConventions.DefaultOpenRouterLlmModel
            : options.Value.LlmModel;

        PlannerLogEvents.LogLlmProviderConfigured(
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
            PlannerConventions.LlmProviders.OpenRouter => LlmProvider.OpenRouter,
            _ => throw new InvalidOperationException(
                $"Unknown LLM provider '{provider}'. Supported: Anthropic, OpenAI, Google, Azure, Ollama, OpenRouter."),
        };
    }
}
