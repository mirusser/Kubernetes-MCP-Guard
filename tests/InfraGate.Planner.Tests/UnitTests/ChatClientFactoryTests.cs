using InfraGate.AgentLlm;
using InfraGate.Planner.Llm;
using Microsoft.Extensions.Options;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class ChatClientFactoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("anthropic")]
    [InlineData("Anthropic")]
    [InlineData("ANTHROPIC")]
    public void Create_AnthropicProvider_ReturnsAnthropicChatClient(string provider)
    {
        var options = Substitute.For<IOptions<PlannerOptions>>();
        options.Value.Returns(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = provider,
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);
        var client = factory.Create();

        Assert.IsType<AnthropicChatClient>(client);
    }

    [Fact]
    public void Create_MissingApiKey_ThrowsInvalidOperationException()
    {
        var options = Substitute.For<IOptions<PlannerOptions>>();
        options.Value.Returns(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = "anthropic",
            LlmApiKey = "",
        });

        var factory = new ChatClientFactory(options);

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("google")]
    [InlineData("azure")]
    [InlineData("ollama")]
    public void Create_ConfiguredFutureProvider_ThrowsNotImplementedException(string provider)
    {
        var options = Substitute.For<IOptions<PlannerOptions>>();
        options.Value.Returns(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = provider,
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);

        Assert.Throws<NotImplementedException>(() => factory.Create());
    }

    [Fact]
    public void Create_WhitespaceProvider_ReturnsAnthropicChatClient()
    {
        var options = Substitute.For<IOptions<PlannerOptions>>();
        options.Value.Returns(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = "   ",
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);
        var client = factory.Create();

        Assert.IsType<AnthropicChatClient>(client);
    }

    [Fact]
    public void Create_OpenRouterProvider_ReturnsChatClient()
    {
        var options = Substitute.For<IOptions<PlannerOptions>>();
        options.Value.Returns(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);
        var client = factory.Create();

        Assert.NotNull(client);
        Assert.IsNotType<AnthropicChatClient>(client);
    }

    [Fact]
    public void Create_OpenRouterProvider_MissingApiKey_ThrowsInvalidOperationException()
    {
        var options = Substitute.For<IOptions<PlannerOptions>>();
        options.Value.Returns(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = PlannerConventions.LlmProviders.OpenRouter,
            LlmApiKey = "",
        });

        var factory = new ChatClientFactory(options);

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }

    [Fact]
    public void Create_UnknownProvider_ThrowsInvalidOperationException()
    {
        var options = Substitute.For<IOptions<PlannerOptions>>();
        options.Value.Returns(new PlannerOptions
        {
            GatewayBaseUrl = "http://localhost:3001/mcp",
            LlmProvider = "unknown-ai",
            LlmApiKey = "test-key",
        });

        var factory = new ChatClientFactory(options);

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }
}
