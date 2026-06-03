using InfraGate.AgentLlm;
using InfraGate.Observer.Llm;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ChatClientFactoryTests
{
    [Fact]
    public void Create_DefaultProvider_ThrowsInvalidOperationException()
    {
        var factory = CreateFactory(new ObserverOptions { LlmProvider = "", LlmModel = "" });

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("Anthropic")]
    [InlineData("ANTHROPIC")]
    public void Create_AnthropicProvider_ThrowsInvalidOperationException(string provider)
    {
        var factory = CreateFactory(new ObserverOptions { LlmProvider = provider, LlmModel = "" });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("OpenRouter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_AnthropicProvider_MissingApiKey_StillThrowsGuardException()
    {
        // The Anthropic guard fires before the API key check.
        var factory = CreateFactory(new ObserverOptions { LlmProvider = "anthropic" }, apiKey: "");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("OpenRouter", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("google")]
    [InlineData("azure")]
    [InlineData("ollama")]
    public void Create_UnsupportedProvider_ThrowsNotSupportedException(string provider)
    {
        var factory = CreateFactory(new ObserverOptions { LlmProvider = provider });

        Assert.Throws<NotSupportedException>(() => factory.Create());
    }

    [Fact]
    public void Create_OpenRouterProvider_ReturnsRateLimitRetryingChatClient()
    {
        var factory = CreateFactory(new ObserverOptions { LlmProvider = ObserverConventions.LlmProviders.OpenRouter });
        var client = factory.Create();

        Assert.IsType<RateLimitRetryingChatClient>(client);
    }

    [Fact]
    public void Create_OpenRouterProvider_MissingApiKey_ThrowsInvalidOperationException()
    {
        var factory = CreateFactory(
            new ObserverOptions { LlmProvider = ObserverConventions.LlmProviders.OpenRouter },
            apiKey: "");

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }

    [Fact]
    public void Create_OpenRouterProvider_WithCustomModel_FormsClient()
    {
        var factory = CreateFactory(
            new ObserverOptions
            {
                LlmProvider = ObserverConventions.LlmProviders.OpenRouter,
                LlmModel = "my-custom-model",
            },
            loggerFactory: NullLoggerFactory.Instance);
        var client = factory.Create();

        Assert.IsType<RateLimitRetryingChatClient>(client);
    }

    [Fact]
    public void Create_OpenRouterProvider_WithLoggerFactory_FormsClient()
    {
        var factory = CreateFactory(
            new ObserverOptions { LlmProvider = ObserverConventions.LlmProviders.OpenRouter },
            loggerFactory: NullLoggerFactory.Instance);
        var client = factory.Create();

        Assert.IsType<RateLimitRetryingChatClient>(client);
    }

    private static ChatClientFactory CreateFactory(
        ObserverOptions observerOptions,
        string apiKey = "test-key",
        ILoggerFactory? loggerFactory = null)
    {
        return new ChatClientFactory(
            Options.Create(observerOptions),
            Options.Create(new OpenRouterOptions { ApiKey = apiKey }),
            loggerFactory);
    }
}
