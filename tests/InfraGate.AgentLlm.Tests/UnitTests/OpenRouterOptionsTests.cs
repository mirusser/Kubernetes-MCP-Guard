namespace InfraGate.AgentLlm.Tests.UnitTests;

public sealed class OpenRouterOptionsTests
{
    [Fact]
    public void Constants_ArePinned()
    {
        Assert.Equal("InfraGate:OpenRouter", OpenRouterOptions.SectionName);
        Assert.Equal("InfraGate:OpenRouter:ApiKey", OpenRouterOptions.ApiKeyConfigurationKey);
        Assert.Equal("InfraGate__OpenRouter__ApiKey", OpenRouterOptions.ApiKeyEnvironmentVariable);
    }

    [Fact]
    public void DefaultOptions_HaveEmptyApiKey()
    {
        var options = new OpenRouterOptions();

        Assert.Equal(string.Empty, options.ApiKey);
    }
}
