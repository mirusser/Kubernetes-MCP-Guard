using System.ClientModel;
using System.Diagnostics.Metrics;
using InfraGate.AgentLlm;
using InfraGate.Planner.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Chat;

namespace InfraGate.Planner.Llm;

internal sealed class ChatClientFactory(IOptions<PlannerOptions> options, Meter? meter = null) : IChatClientFactory
{

    public IChatClient Create()
    {
        var provider = ParseProvider(options.Value.LlmProvider);

        return provider switch
        {
            LlmProvider.Anthropic => CreateAnthropicClient(),
            LlmProvider.OpenRouter => CreateOpenRouterClient(),
            // Future provider arms are visible here for wiring.
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

        var counter = PlannerMetrics.CreateLlmTokensCounter(meter);
        return new AnthropicChatClient(httpClient, model, NullLoggerFactory.Instance, counter);
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

        var chatClient = new ChatClient(
            model,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") });

        return chatClient.AsIChatClient();
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
