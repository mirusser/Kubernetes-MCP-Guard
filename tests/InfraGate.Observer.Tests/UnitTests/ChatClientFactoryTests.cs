using InfraGate.AgentLlm;
using InfraGate.Observer.Llm;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class ChatClientFactoryTests
{
    [Fact]
    public void Create_DefaultProvider_ThrowsInvalidOperationException()
    {
        var options = Substitute.For<IOptions<ObserverOptions>>();
        options.Value.Returns(new ObserverOptions
        {
            LlmProvider = "",
            LlmModel = "",
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("Anthropic")]
    [InlineData("ANTHROPIC")]
    public void Create_AnthropicProvider_ThrowsInvalidOperationException(string provider)
    {
        var options = Substitute.For<IOptions<ObserverOptions>>();
        options.Value.Returns(new ObserverOptions
        {
            LlmProvider = provider,
            LlmModel = "",
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("OpenRouter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_AnthropicProvider_MissingApiKey_StillThrowsGuardException()
    {
        // The Anthropic guard fires before the API key check.
        var options = Substitute.For<IOptions<ObserverOptions>>();
        options.Value.Returns(new ObserverOptions
        {
            LlmProvider = "anthropic",
            LlmApiKey = "",
        });

        var factory = new ChatClientFactory(options);

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
        var options = Substitute.For<IOptions<ObserverOptions>>();
        options.Value.Returns(new ObserverOptions
        {
            LlmProvider = provider,
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);

        Assert.Throws<NotSupportedException>(() => factory.Create());
    }

    [Fact]
    public void Create_OpenRouterProvider_ReturnsRateLimitRetryingChatClient()
    {
        var options = Substitute.For<IOptions<ObserverOptions>>();
        options.Value.Returns(new ObserverOptions
        {
            LlmProvider = ObserverConventions.LlmProviders.OpenRouter,
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);
        var client = factory.Create();

        Assert.IsType<RateLimitRetryingChatClient>(client);
    }

    [Fact]
    public void Create_OpenRouterProvider_MissingApiKey_ThrowsInvalidOperationException()
    {
        var options = Substitute.For<IOptions<ObserverOptions>>();
        options.Value.Returns(new ObserverOptions
        {
            LlmProvider = ObserverConventions.LlmProviders.OpenRouter,
            LlmApiKey = "",
        });

        var factory = new ChatClientFactory(options);

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }

    [Fact]
    public void Create_UnknownProvider_ThrowsInvalidOperationException()
    {
        var options = Substitute.For<IOptions<ObserverOptions>>();
        options.Value.Returns(new ObserverOptions
        {
            LlmProvider = "mythical-ai",
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }
}
