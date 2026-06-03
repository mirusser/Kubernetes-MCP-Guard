using InfraGate.AgentLlm;
using InfraGate.Planner.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class ChatClientFactoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("anthropic")]
    [InlineData("Anthropic")]
    [InlineData("ANTHROPIC")]
    public void Create_AnthropicProvider_ThrowsInvalidOperationException(string provider)
    {
        var factory = CreateFactory(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = provider,
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("OpenRouter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_AnthropicProvider_MissingApiKey_StillThrowsGuardException()
    {
        // The Anthropic guard fires before the API key check.
        var factory = CreateFactory(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = "anthropic",
        }, apiKey: "");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("OpenRouter", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("google")]
    [InlineData("azure")]
    [InlineData("ollama")]
    public void Create_ConfiguredFutureProvider_ThrowsNotImplementedException(string provider)
    {
        var factory = CreateFactory(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = provider,
        });

        Assert.Throws<NotImplementedException>(() => factory.Create());
    }

    [Fact]
    public void Create_WhitespaceProvider_ThrowsInvalidOperationException()
    {
        // Whitespace resolves to Anthropic, which is now guarded.
        var factory = CreateFactory(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = "   ",
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("OpenRouter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_OpenRouterProvider_ReturnsRateLimitRetryingChatClient()
    {
        var factory = CreateFactory(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
        });
        var client = factory.Create();

        Assert.IsType<RateLimitRetryingChatClient>(client);
    }

    [Fact]
    public void Create_OpenRouterProvider_MissingApiKey_ThrowsInvalidOperationException()
    {
        var factory = CreateFactory(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
        }, apiKey: "");

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }

    [Fact]
    public void Create_OpenRouterProvider_WithLoggerFactory_FormsClient()
    {
        var factory = CreateFactory(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
        }, loggerFactory: NullLoggerFactory.Instance);
        var client = factory.Create();

        Assert.IsType<RateLimitRetryingChatClient>(client);
    }

    [Fact]
    public void Create_OpenRouterProvider_WithCustomModel_FormsClient()
    {
        var factory = CreateFactory(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
            LlmModel = "custom-model",
        }, loggerFactory: NullLoggerFactory.Instance);
        var client = factory.Create();

        Assert.IsType<RateLimitRetryingChatClient>(client);
    }

    [Fact]
    public void Create_UnknownProvider_ThrowsInvalidOperationException()
    {
        var factory = CreateFactory(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = "unknown-ai",
        });

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }

    private static ChatClientFactory CreateFactory(
        PlannerOptions plannerOptions,
        string apiKey = "test-key",
        ILoggerFactory? loggerFactory = null)
    {
        return new ChatClientFactory(
            Options.Create(plannerOptions),
            Options.Create(new OpenRouterOptions { ApiKey = apiKey }),
            loggerFactory);
    }
}
